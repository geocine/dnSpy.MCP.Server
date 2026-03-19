/*
    Copyright (C) 2026 @chichicaste

    This file is part of dnSpy MCP Server module.

    dnSpy MCP Server is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy MCP Server is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy MCP Server.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.TreeView;
using dnSpy.MCP.Server.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace dnSpy.MCP.Server.Application
{
    /// <summary>
    /// Provides run_script: execute arbitrary C# code via Roslyn inside dnSpy's process.
    ///
    /// Scripts have access to:
    ///   module       — ModuleDef? of the currently selected assembly (dnlib)
    ///   allModules   — IReadOnlyList&lt;ModuleDef&gt; of all loaded assemblies
    ///   print(...)   — append text to the output returned to the MCP caller
    ///   docService   — IDsDocumentService for loading/managing assemblies
    ///   dbgManager   — DbgManager? (null when no debug session is active)
    ///
    /// Standard namespaces pre-imported:
    ///   System, System.Linq, System.IO, System.Collections.Generic,
    ///   dnlib.DotNet, dnlib.DotNet.Emit, dnlib.DotNet.Writer
    ///
    /// The script return value (if any) is appended to the output as "Return: &lt;value&gt;".
    /// </summary>
    [Export(typeof(ScriptTools))]
    public sealed class ScriptTools
    {
        internal const int CodeModeToolCallLimit = 24;
        readonly IDsDocumentService documentService;
        readonly IDocumentTreeView documentTreeView;
        readonly Lazy<DbgManager> dbgManager;
        readonly Lazy<IDocumentTabService> documentTabService;
        static readonly IReadOnlyList<CodeModeExample> codeModeExamples = BuildCodeModeExamples();

        [ImportingConstructor]
        public ScriptTools(
            IDsDocumentService documentService,
            IDocumentTreeView documentTreeView,
            Lazy<DbgManager> dbgManager,
            Lazy<IDocumentTabService> documentTabService)
        {
            this.documentService = documentService;
            this.documentTreeView = documentTreeView;
            this.dbgManager = dbgManager;
            this.documentTabService = documentTabService;
        }

        // ─────────────────────────────────────────────────────────────────────
        // run_script
        // ─────────────────────────────────────────────────────────────────────
        public string RunScript(Dictionary<string, object>? args)
        {
            if (!Configuration.McpConfig.Instance.EnableRunScript)
                throw new InvalidOperationException(
                    "run_script is disabled. Set \"enableRunScript\": true in mcp-config.json to enable.");

            string? code = null;
            if (args != null && args.TryGetValue("code", out var codeVal))
                code = codeVal?.ToString();
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("'code' is required.");
            int timeoutSeconds = (args != null && args.TryGetValue("timeout_seconds", out var to))
                ? Convert.ToInt32(to) : 30;

            var globals = BuildGlobals();
            var opts = BuildScriptOptions();

            var sb = globals.Output;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var task = CSharpScript.RunAsync(code!, opts, globals, typeof(McpScriptGlobals), cts.Token);
                task.Wait(cts.Token);

                var state = task.Result;
                var retVal = state.ReturnValue;
                if (retVal != null && retVal.GetType() != typeof(void))
                {
                    string retStr = retVal is System.Collections.IEnumerable en && !(retVal is string)
                        ? string.Join("\n", en.Cast<object>().Select(o => o?.ToString()))
                        : retVal.ToString()!;
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append("Return: ").AppendLine(retStr);
                }
            }
            catch (OperationCanceledException)
            {
                sb.AppendLine($"[!] Script timed out after {timeoutSeconds}s.");
            }
            catch (AggregateException aex)
            {
                var inner = aex.InnerException ?? aex;
                if (inner is CompilationErrorException cee)
                {
                    sb.AppendLine("[!] Compilation errors:");
                    foreach (var diag in cee.Diagnostics)
                        sb.AppendLine($"  {diag}");
                }
                else if (inner is OutOfMemoryException)
                {
                    sb.AppendLine("[!] Script error: OutOfMemoryException — the script consumed too much memory.");
                    sb.AppendLine("    Tip: limit the scope of your query (e.g. filter by namespace, avoid loading all methods at once).");
                }
                else
                {
                    sb.AppendLine($"[!] Script error: {inner.GetType().Name}: {inner.Message}");
                    if (inner.StackTrace is string st)
                        sb.AppendLine(st.Split('\n').Take(6).Aggregate("", (a, b) => a + b + "\n"));
                }
            }
            catch (OutOfMemoryException)
            {
                // OutOfMemoryException can escape AggregateException in some Roslyn paths.
                sb.AppendLine("[!] Script error: OutOfMemoryException — the script consumed too much memory.");
                sb.AppendLine("    Tip: limit the scope of your query (e.g. filter by namespace, avoid loading all methods at once).");
            }
            catch (Exception ex)
            {
                // Catch-all: prevent any unhandled exception from crashing the dnSpy process.
                sb.AppendLine($"[!] Script error: {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace is string st2)
                    sb.AppendLine(st2.Split('\n').Take(6).Aggregate("", (a, b) => a + b + "\n"));
            }

            return sb.Length == 0 ? "(no output)" : sb.ToString();
        }

        static ScriptOptions? cachedCodeModeOptions;

        public CallToolResult ExecuteCode(
            Dictionary<string, object>? args,
            string? sessionId,
            Func<string, Dictionary<string, object>?, string?, bool, CallToolResult> toolInvoker)
        {
            string? code = null;
            if (args != null && args.TryGetValue("code", out var codeVal))
                code = codeVal?.ToString();
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("'code' is required.");

            var timeoutSeconds = 20;
            if (args != null && args.TryGetValue("timeout_seconds", out var timeoutValue)) {
                if (timeoutValue is JsonElement timeoutElement && timeoutElement.ValueKind == JsonValueKind.Number && timeoutElement.TryGetInt32(out var jsonTimeout))
                    timeoutSeconds = jsonTimeout;
                else if (int.TryParse(timeoutValue?.ToString(), out var parsedTimeout))
                    timeoutSeconds = parsedTimeout;
            }

            timeoutSeconds = Math.Max(1, Math.Min(120, timeoutSeconds));

            var globals = new McpCodeModeGlobals(toolInvoker, sessionId);
            var options = BuildCodeModeScriptOptions();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try {
                var task = CSharpScript.RunAsync(code!, options, globals, typeof(McpCodeModeGlobals), cts.Token);
                task.Wait(cts.Token);

                var state = task.Result;
                var payload = new {
                    session_id = sessionId,
                    timed_out = false,
                    output = globals.OutputLines.ToList(),
                    output_text = globals.Output.Length == 0 ? string.Empty : globals.Output.ToString(),
                    return_value = SimplifyScriptValue(state.ReturnValue),
                    tool_calls = globals.ToolCalls.ToList()
                };
                return ToolResponseFactory.Json(payload);
            }
            catch (OperationCanceledException) {
                return ToolResponseFactory.Json(new {
                    session_id = sessionId,
                    timed_out = true,
                    output = globals.OutputLines.ToList(),
                    output_text = globals.Output.ToString(),
                    return_value = (object?)null,
                    tool_calls = globals.ToolCalls.ToList(),
                    error = $"execute_code timed out after {timeoutSeconds} second(s)."
                }, true);
            }
            catch (AggregateException aggregateException) {
                var inner = aggregateException.InnerException ?? aggregateException;
                if (inner is CompilationErrorException compilationError) {
                    var diagnostics = compilationError.Diagnostics.Select(a => a.ToString()).ToList();
                    return ToolResponseFactory.Json(new {
                        session_id = sessionId,
                        timed_out = false,
                        output = globals.OutputLines.ToList(),
                        output_text = globals.Output.ToString(),
                        tool_calls = globals.ToolCalls.ToList(),
                        error = "Compilation failed",
                        diagnostics,
                        likely_fixes = BuildLikelyFixes(diagnostics, DetectReferencedTools(code!))
                    }, true);
                }

                return ToolResponseFactory.Json(new {
                    session_id = sessionId,
                    timed_out = false,
                    output = globals.OutputLines.ToList(),
                    output_text = globals.Output.ToString(),
                    tool_calls = globals.ToolCalls.ToList(),
                    error = $"{inner.GetType().Name}: {inner.Message}"
                }, true);
            }
            catch (CompilationErrorException compilationError) {
                var diagnostics = compilationError.Diagnostics.Select(a => a.ToString()).ToList();
                return ToolResponseFactory.Json(new {
                    session_id = sessionId,
                    timed_out = false,
                    output = globals.OutputLines.ToList(),
                    output_text = globals.Output.ToString(),
                    tool_calls = globals.ToolCalls.ToList(),
                    error = "Compilation failed",
                    diagnostics,
                    likely_fixes = BuildLikelyFixes(diagnostics, DetectReferencedTools(code!))
                }, true);
            }
            catch (Exception ex) {
                return ToolResponseFactory.Json(new {
                    session_id = sessionId,
                    timed_out = false,
                    output = globals.OutputLines.ToList(),
                    output_text = globals.Output.ToString(),
                    tool_calls = globals.ToolCalls.ToList(),
                    error = $"{ex.GetType().Name}: {ex.Message}"
                }, true);
            }
        }

        public CallToolResult GetCodeModeGuide(Dictionary<string, object>? args)
        {
            var topic = args != null && args.TryGetValue("topic", out var topicObj)
                ? topicObj?.ToString()?.Trim()
                : null;

            var payload = new Dictionary<string, object?> {
                ["summary"] = "Use dnspy_execute_code as the preferred first tool for multi-step reconstruction workflows. Inside code mode, prefer await call_tool(...) to orchestrate discovery, source recovery, xrefs, renaming, and runtime inspection, including tools that are not directly exposed in tools/list yet.",
                ["recommended_workflow"] = new[] {
                    "Start with dnspy_execute_code when the task requires more than one dnSpy tool call.",
                    "Inside code mode, use await call_tool(\"dnspy_search_tools\", ...) or await call_tool(\"dnspy_get_tool_schemas\", ...) to discover tools without widening the direct session surface.",
                    "Enable a tool group only if you want that workflow area directly visible through tools/list and directly callable outside code mode.",
                    "Validate larger snippets with dnspy_validate_code_snippet before execution when the flow is non-trivial."
                },
                ["helpers"] = new[] {
                    new {
                        name = "print",
                        signature = "void print(object? value = null)",
                        description = "Append a line of text to the code-mode output."
                    },
                    new {
                        name = "call_tool",
                        signature = "Task<object?> call_tool(string name, object? args = null)",
                        description = "Invoke another dnSpy MCP tool and get structuredContent or fallback text."
                    }
                },
                ["limits"] = new {
                    max_timeout_seconds = 120,
                    default_timeout_seconds = 20,
                    max_tool_calls = CodeModeToolCallLimit,
                    notes = new[] {
                        "dnspy_execute_code cannot recursively invoke dnspy_execute_code or dnspy_run_script.",
                        "Prefer one composed script for a workflow instead of many tiny execute_code calls."
                    }
                },
                ["common_patterns"] = new[] {
                    new {
                        name = "Search then decompile",
                        snippet = "var tools = await call_tool(\"dnspy_search_tools\", new { query = \"file format parser\", detail = \"detailed\" });\nvar src = await call_tool(\"dnspy_get_decompiled_source\", new { member_id = \"<member-id>\" });\nprint(JsonSerializer.Serialize(src));"
                    },
                    new {
                        name = "Find string references",
                        snippet = "var refs = await call_tool(\"dnspy_find_string_references\", new { assembly_name = asm, file_path = path, string_value = \"palette\" });\nprint(JsonSerializer.Serialize(refs));"
                    },
                    new {
                        name = "Attach and inspect",
                        snippet = "await call_tool(\"dnspy_attach_to_process\", new { process_id = 1234 });\nawait call_tool(\"dnspy_break_debugger\");\nvar locals = await call_tool(\"dnspy_get_local_variables\");\nprint(JsonSerializer.Serialize(locals));"
                    }
                },
                ["common_failures"] = new[] {
                    new {
                        problem = "Unknown tool names",
                        fix = "Run dnspy_search_tools or dnspy_get_tool_schemas first and use the public dnspy_* tool name."
                    },
                    new {
                        problem = "Trying to call hidden tools directly",
                        fix = "Enable the correct tool group or invoke the tool from dnspy_execute_code."
                    },
                    new {
                        problem = "Compiling snippets with missing semicolons or malformed anonymous objects",
                        fix = "Run dnspy_validate_code_snippet and inspect diagnostics before executing."
                    },
                    new {
                        problem = "Calling call_tool without await",
                        fix = "call_tool returns Task<object?>. Use await unless you intentionally want the Task."
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(topic))
            {
                payload["topic"] = topic;
                payload["example_matches"] = FilterCodeModeExamples(topic, 4)
                    .Select(CreateCodeExamplePayload)
                    .ToList();
            }

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetCodeExamples(Dictionary<string, object>? args)
        {
            var task = args != null && args.TryGetValue("task", out var taskObj)
                ? taskObj?.ToString()
                : null;
            var limit = 4;
            if (args != null && args.TryGetValue("limit", out var limitObj))
            {
                if (limitObj is JsonElement limitElement && limitElement.ValueKind == JsonValueKind.Number && limitElement.TryGetInt32(out var jsonLimit))
                    limit = jsonLimit;
                else if (int.TryParse(limitObj?.ToString(), out var parsedLimit))
                    limit = parsedLimit;
            }

            limit = Math.Max(1, Math.Min(10, limit));

            var items = FilterCodeModeExamples(task, limit)
                .Select(CreateCodeExamplePayload)
                .ToList();

            return ToolResponseFactory.Json(new {
                task = task ?? string.Empty,
                returned_count = items.Count,
                items
            });
        }

        public CallToolResult ValidateCodeSnippet(Dictionary<string, object>? args)
        {
            string? code = null;
            if (args != null && args.TryGetValue("code", out var codeObj))
                code = codeObj?.ToString();
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("'code' is required.");

            var options = BuildCodeModeScriptOptions();
            var script = CSharpScript.Create(code!, options, typeof(McpCodeModeGlobals));
            var diagnostics = script.Compile().OrderBy(a => a.Severity).ThenBy(a => a.Location.SourceSpan.Start).ToList();
            var referencedTools = DetectReferencedTools(code!);
            var toolChecks = referencedTools.Select(a => {
                var isKnown = ToolNameMapper.TryMapPublicToInternal(a, out var internalName);
                var group = isKnown ? ToolCatalogMetadata.GetGroup(internalName) : "unknown";
                return new {
                    tool_name = a,
                    is_known = isKnown,
                    required_group = group,
                    is_bootstrap = isKnown && ToolCatalogMetadata.IsBootstrapTool(internalName)
                };
            }).ToList();

            var errorDiagnostics = diagnostics.Where(a => a.Severity == DiagnosticSeverity.Error).ToList();
            var warningDiagnostics = diagnostics.Where(a => a.Severity == DiagnosticSeverity.Warning).ToList();
            var diagnosticPayload = diagnostics.Select(a => new {
                id = a.Id,
                severity = a.Severity.ToString(),
                message = a.GetMessage(),
                span = new {
                    start = a.Location.SourceSpan.Start,
                    length = a.Location.SourceSpan.Length
                }
            }).ToList();

            return ToolResponseFactory.Json(new {
                is_valid = errorDiagnostics.Count == 0,
                error_count = errorDiagnostics.Count,
                warning_count = warningDiagnostics.Count,
                referenced_tools = toolChecks,
                diagnostics = diagnosticPayload,
                likely_fixes = BuildLikelyFixes(diagnosticPayload.Select(a => $"{a.id}: {a.message}").ToList(), referencedTools)
            }, errorDiagnostics.Count > 0);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Globals object passed to every script
        // ─────────────────────────────────────────────────────────────────────
        McpScriptGlobals BuildGlobals()
        {
            // Resolve currently selected module
            ModuleDef? selectedModule = null;
            try
            {
                var node = documentTreeView.TreeView.SelectedItem as DocumentTreeNodeData;
                var doc = (node as DsDocumentNode)?.Document
                    ?? node?.GetAncestorOrSelf<DsDocumentNode>()?.Document;
                selectedModule = doc?.ModuleDef;
            }
            catch { /* no selection */ }

            var allMods = documentService.GetDocuments()
                .Select(d => d.ModuleDef)
                .Where(m => m != null)
                .ToList()!;

            DbgManager? dbg = null;
            try { dbg = dbgManager.Value; } catch { }

            return new McpScriptGlobals(selectedModule, allMods!, documentService, dbg);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ScriptOptions: references + default imports
        // ─────────────────────────────────────────────────────────────────────
        static ScriptOptions? _cachedOptions;
        static ScriptOptions BuildScriptOptions()
        {
            if (_cachedOptions != null) return _cachedOptions;

            // Collect assemblies to reference from the current AppDomain
            static Assembly? TryLoad(string name)
            {
                try { return Assembly.Load(name); } catch { return null; }
            }

            var refs = new List<Assembly?>
            {
                typeof(object).Assembly,                        // mscorlib / System.Runtime
                typeof(Enumerable).Assembly,                    // System.Linq
                typeof(File).Assembly,                          // System.IO
                typeof(JsonSerializer).Assembly,                // System.Text.Json
                typeof(ModuleDef).Assembly,                     // dnlib
                typeof(IDsDocumentService).Assembly,            // dnSpy.Contracts.DnSpy
                typeof(DbgManager).Assembly,                    // dnSpy.Contracts.Debugger
                TryLoad("System.Collections"),
                TryLoad("System.Text.RegularExpressions"),
                TryLoad("Newtonsoft.Json"),
                // Current assembly (MCP server) so scripts can access McpScriptGlobals
                typeof(ScriptTools).Assembly,
            };

            // Also add every already-loaded assembly — makes all dnSpy APIs available
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location))
                        refs.Add(asm);
                }
                catch { }
            }

            _cachedOptions = ScriptOptions.Default
                .WithReferences(refs.OfType<Assembly>()
                    .GroupBy(a => a!.FullName).Select(g => g.First()))
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.IO",
                    "System.Text",
                    "System.Collections.Generic",
                    "System.Reflection",
                    "System.Threading",
                    "System.Threading.Tasks",
                    "System.Text.Json",
                    "dnlib.DotNet",
                    "dnlib.DotNet.Emit",
                    "dnlib.DotNet.Writer",
                    "dnSpy.Contracts.Documents",
                    "dnSpy.Contracts.Debugger");

            return _cachedOptions;
        }

        static ScriptOptions BuildCodeModeScriptOptions()
        {
            if (cachedCodeModeOptions != null)
                return cachedCodeModeOptions;

            cachedCodeModeOptions = ScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(List<>).Assembly,
                    typeof(Task).Assembly,
                    typeof(JsonSerializer).Assembly,
                    typeof(ScriptTools).Assembly)
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Text.Json",
                    "System.Threading.Tasks");

            return cachedCodeModeOptions;
        }

        static IReadOnlyList<CodeModeExample> BuildCodeModeExamples() =>
            new[] {
                new CodeModeExample(
                    "find-custom-file-parser",
                    "Find custom file format parsing entry points",
                    "Use this when you want to trace how a game or app reads a proprietary format, converts bytes into objects, and hands them to downstream systems.",
                    new[] { "reconstruction_core", "source_and_decompile", "architecture_and_xrefs" },
                    new[] { "file parser", "format reader", "binary reader", "custom format", "game asset" },
                    "var asm = @\"D:\\\\path\\\\game.exe\";\nvar hits = await call_tool(\"dnspy_search_string_literals\", new { file_path = asm, query = \"palette\" });\nprint(JsonSerializer.Serialize(hits));\nvar refs = await call_tool(\"dnspy_find_string_references\", new { file_path = asm, string_value = \"palette\" });\nprint(JsonSerializer.Serialize(refs));"),
                new CodeModeExample(
                    "trace-render-pipeline",
                    "Trace the render/update pipeline",
                    "Use this when you already found a scene, renderer, or game loop type and want to decompile the hot path methods in one pass.",
                    new[] { "source_and_decompile", "architecture_and_xrefs" },
                    new[] { "render loop", "game loop", "draw", "update", "spritebatch" },
                    "var asmName = \"Game\";\nvar asmPath = @\"D:\\\\path\\\\game.exe\";\nasync Task Dump(string type, string method) {\n  var src = await call_tool(\"dnspy_decompile_method\", new { assembly_name = asmName, file_path = asmPath, type_full_name = type, method_name = method });\n  print($\"===== {type}.{method} =====\");\n  print(src?.ToString());\n}\nawait Dump(\"Renderer\", \"LoadContent\");\nawait Dump(\"Renderer\", \"Update\");\nawait Dump(\"Renderer\", \"Draw\");"),
                new CodeModeExample(
                    "rename-obfuscated-symbols",
                    "Generate and apply symbol rename candidates",
                    "Use this when names are obfuscated and you want to infer intent from usage and patch back safer names incrementally.",
                    new[] { "deobfuscation_and_recovery", "editing_and_patchback" },
                    new[] { "rename", "obfuscated", "semantic labels", "patchback" },
                    "var suggestions = await call_tool(\"dnspy_suggest_symbol_renames\", new { file_path = @\"D:\\\\path\\\\game.exe\", limit = 10 });\nprint(JsonSerializer.Serialize(suggestions));\n// Apply only after review:\n// await call_tool(\"dnspy_rename_symbol\", new { member_id = \"<member-id>\", new_name = \"ReadPaletteChunk\" });"),
                new CodeModeExample(
                    "attach-and-inspect-runtime",
                    "Attach, pause, and inspect runtime values",
                    "Use this when static analysis is insufficient and you need to confirm the actual decode path or inspect live object state.",
                    new[] { "debug_runtime", "memory_and_dumping" },
                    new[] { "attach", "debug", "runtime", "locals", "memory" },
                    "await call_tool(\"dnspy_attach_to_process\", new { process_id = 1234 });\nawait call_tool(\"dnspy_break_debugger\");\nvar frames = await call_tool(\"dnspy_get_call_stack\");\nprint(JsonSerializer.Serialize(frames));\nvar locals = await call_tool(\"dnspy_get_local_variables\");\nprint(JsonSerializer.Serialize(locals));")
            };

        static List<CodeModeExample> FilterCodeModeExamples(string? task, int limit)
        {
            if (string.IsNullOrWhiteSpace(task))
                return codeModeExamples.Take(limit).ToList();

            var tokens = Tokenize(task);
            return codeModeExamples
                .Select(a => new {
                    Example = a,
                    Score = ScoreExample(a, task, tokens)
                })
                .Where(a => a.Score > 0)
                .OrderByDescending(a => a.Score)
                .ThenBy(a => a.Example.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(a => a.Example)
                .ToList();
        }

        static object CreateCodeExamplePayload(CodeModeExample example) => new {
            id = example.Id,
            title = example.Title,
            when_to_use = example.WhenToUse,
            recommended_groups = example.RecommendedGroups,
            keywords = example.Keywords,
            snippet = example.Snippet
        };

        static int ScoreExample(CodeModeExample example, string query, IReadOnlyList<string> tokens)
        {
            var score = 0;
            var title = example.Title.ToLowerInvariant();
            var description = example.WhenToUse.ToLowerInvariant();

            if (title.Contains(query.ToLowerInvariant()))
                score += 8;
            if (description.Contains(query.ToLowerInvariant()))
                score += 4;

            foreach (var token in tokens)
            {
                if (title.Contains(token))
                    score += 3;
                if (description.Contains(token))
                    score += 2;
                if (example.Keywords.Any(a => a.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    score += 4;
                if (example.RecommendedGroups.Any(a => a.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                    score += 2;
            }

            return score;
        }

        static List<string> Tokenize(string? query) =>
            string.IsNullOrWhiteSpace(query)
                ? new List<string>()
                : query.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', ':', '/', '\\', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().ToLowerInvariant())
                    .Where(a => a.Length >= 2)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

        static List<string> BuildLikelyFixes(IReadOnlyList<string> diagnostics, IReadOnlyList<string> referencedTools)
        {
            var fixes = new List<string>();

            if (referencedTools.Count > 0)
            {
                foreach (var tool in referencedTools)
                {
                    if (!ToolNameMapper.TryMapPublicToInternal(tool, out var internalName))
                        fixes.Add($"Unknown tool '{tool}'. Use dnspy_search_tools or dnspy_get_tool_schemas to confirm the public dnspy_* name.");
                    else if (!ToolCatalogMetadata.IsBootstrapTool(internalName))
                        fixes.Add($"'{tool}' belongs to tool group '{ToolCatalogMetadata.GetGroup(internalName)}'. Enable that group if you want direct visibility outside code mode.");
                }
            }

            if (diagnostics.Any(a => a.IndexOf("The name 'call_tool' does not exist", StringComparison.OrdinalIgnoreCase) >= 0))
                fixes.Add("call_tool is only available inside dnspy_execute_code and dnspy_validate_code_snippet.");
            if (diagnostics.Any(a => a.IndexOf("The name 'print' does not exist", StringComparison.OrdinalIgnoreCase) >= 0))
                fixes.Add("print is only available inside dnspy_execute_code and dnspy_validate_code_snippet.");
            if (diagnostics.Any(a => a.IndexOf("; expected", StringComparison.OrdinalIgnoreCase) >= 0))
                fixes.Add("Check for missing semicolons or malformed anonymous objects like new { file_path = asm }.");
            if (diagnostics.Any(a => a.IndexOf(") expected", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("} expected", StringComparison.OrdinalIgnoreCase) >= 0))
                fixes.Add("Check for unmatched parentheses, braces, or interpolated string delimiters.");
            if (referencedTools.Count == 0)
                fixes.Add("If this snippet is meant to orchestrate dnSpy tools, use await call_tool(\"dnspy_*\", new { ... }).");

            return fixes.Distinct(StringComparer.Ordinal).ToList();
        }

        static IReadOnlyList<string> DetectReferencedTools(string code)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(kind: SourceCodeKind.Script));
                var root = tree.GetRoot();
                var names = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(a => a.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "call_tool")
                    .Select(a => a.ArgumentList.Arguments.FirstOrDefault()?.Expression)
                    .OfType<LiteralExpressionSyntax>()
                    .Where(a => a.IsKind(SyntaxKind.StringLiteralExpression))
                    .Select(a => a.Token.ValueText)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (names.Count > 0)
                    return names;
            }
            catch
            {
            }

            return Regex.Matches(code, "call_tool\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(a => a.Groups[1].Value)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        static object? SimplifyScriptValue(object? value)
        {
            if (value == null)
                return null;

            if (value is string || value is bool || value is byte || value is sbyte ||
                value is short || value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float || value is double ||
                value is decimal)
                return value;

            if (value is JsonElement element)
                return JsonSerializer.Deserialize<object>(element.GetRawText());

            try {
                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<object>(json);
            }
            catch {
                return value.ToString();
            }
        }
    }

    sealed class CodeModeExample
    {
        public CodeModeExample(string id, string title, string whenToUse, IReadOnlyList<string> recommendedGroups, IReadOnlyList<string> keywords, string snippet)
        {
            Id = id;
            Title = title;
            WhenToUse = whenToUse;
            RecommendedGroups = recommendedGroups;
            Keywords = keywords;
            Snippet = snippet;
        }

        public string Id { get; }
        public string Title { get; }
        public string WhenToUse { get; }
        public IReadOnlyList<string> RecommendedGroups { get; }
        public IReadOnlyList<string> Keywords { get; }
        public string Snippet { get; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Globals class exposed to every script
    // ─────────────────────────────────────────────────────────────────────────
    public sealed class McpScriptGlobals
    {
        // ── State ──────────────────────────────────────────────────────────
        internal readonly StringBuilder Output = new();

        // ── Script-visible globals ─────────────────────────────────────────

        /// <summary>Currently selected assembly's module, or null if none.</summary>
        public ModuleDef? module { get; }

        /// <summary>All loaded modules (all open assemblies).</summary>
        public IReadOnlyList<ModuleDef> allModules { get; }

        /// <summary>dnSpy document service — load/enumerate assemblies.</summary>
        public IDsDocumentService docService { get; }

        /// <summary>Active debugger manager, or null when no session is open.</summary>
        public DbgManager? dbgManager { get; }

        internal McpScriptGlobals(
            ModuleDef? module,
            IReadOnlyList<ModuleDef> allModules,
            IDsDocumentService docService,
            DbgManager? dbgManager)
        {
            this.module = module;
            this.allModules = allModules;
            this.docService = docService;
            this.dbgManager = dbgManager;
        }

        // ── Output helpers ─────────────────────────────────────────────────

        public void print(object? value = null) =>
            Output.AppendLine(value?.ToString() ?? "");

        public void print(string fmt, params object[] args) =>
            Output.AppendLine(string.Format(fmt, args));

        public void print(System.Collections.IEnumerable items)
        {
            foreach (var item in items)
                Output.AppendLine(item?.ToString() ?? "(null)");
        }
    }

    public sealed class McpCodeModeGlobals
    {
        readonly Func<string, Dictionary<string, object>?, string?, bool, CallToolResult> toolInvoker;
        readonly string? sessionId;
        int toolCallCount;

        internal readonly StringBuilder Output = new();
        internal readonly List<string> OutputLines = new List<string>();
        internal readonly List<object> ToolCalls = new List<object>();

        public McpCodeModeGlobals(
            Func<string, Dictionary<string, object>?, string?, bool, CallToolResult> toolInvoker,
            string? sessionId)
        {
            this.toolInvoker = toolInvoker;
            this.sessionId = sessionId;
        }

        public void print(object? value = null)
        {
            var text = value?.ToString() ?? string.Empty;
            OutputLines.Add(text);
            Output.AppendLine(text);
        }

        public async Task<object?> call_tool(string name, object? arguments = null)
        {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tool name cannot be empty.");

            toolCallCount++;
            if (toolCallCount > ScriptTools.CodeModeToolCallLimit)
                throw new InvalidOperationException($"execute_code exceeded the maximum of {ScriptTools.CodeModeToolCallLimit} tool calls.");

            var normalizedArguments = NormalizeArguments(arguments);
            var result = toolInvoker(name, normalizedArguments, sessionId, true);
            var text = result.Content == null ? string.Empty : string.Join("\n", result.Content.Select(a => a.Text).Where(a => !string.IsNullOrWhiteSpace(a)));
            ToolCalls.Add(new {
                name,
                is_error = result.IsError,
                arguments = normalizedArguments,
                text
            });

            if (result.IsError)
                throw new InvalidOperationException($"Tool {name} failed: {text}");

            return result.StructuredContent ?? (string.IsNullOrWhiteSpace(text) ? null : text);
        }

        static Dictionary<string, object>? NormalizeArguments(object? arguments)
        {
            if (arguments == null)
                return null;

            if (arguments is Dictionary<string, object> dictionary)
                return dictionary;

            if (arguments is JsonElement element && element.ValueKind == JsonValueKind.Object)
                return JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());

            var json = JsonSerializer.Serialize(arguments);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Tool arguments must serialize to a JSON object.");
            return JsonSerializer.Deserialize<Dictionary<string, object>>(document.RootElement.GetRawText());
        }
    }
}
