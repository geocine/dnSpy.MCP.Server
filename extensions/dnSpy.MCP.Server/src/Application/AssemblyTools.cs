/*
    Copyright (C) 2026 @chichicaste
    Modifications Copyright (C) 2026 @geocine

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
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using dnlib.DotNet.Resources;
using dnlib.PE;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.DotNet.Evaluation;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.MCP.Server.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace dnSpy.MCP.Server.Application
{
    /// <summary>
    /// Assembly-focused utilities extracted from McpTools.
    /// Provides: list_assemblies, get_assembly_info, list_types, list_native_modules,
    /// scan_pe_strings, and load_assembly.
    /// </summary>
    [Export(typeof(AssemblyTools))]
    public sealed class AssemblyTools
    {
        static readonly Dictionary<string, Dictionary<string, object?>> astOutlineCache = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        static readonly Queue<string> astOutlineCacheOrder = new Queue<string>();
        static readonly object astOutlineCacheLock = new object();
        const int MaxAstOutlineCacheEntries = 64;

        readonly IDocumentTreeView documentTreeView;
        readonly IDsDocumentService documentService;
        readonly Lazy<DbgManager> dbgManager;
        readonly Lazy<IDocumentTabService> documentTabService;
        readonly IDecompilerService decompilerService;
        readonly Lazy<IBamlDecompiler>? bamlDecompiler;
        readonly Lazy<IXamlOutputOptionsProvider>? xamlOutputOptionsProvider;

        [ImportingConstructor]
        public AssemblyTools(
            IDocumentTreeView documentTreeView,
            IDsDocumentService documentService,
            Lazy<DbgManager> dbgManager,
            Lazy<IDocumentTabService> documentTabService,
            IDecompilerService decompilerService,
            [ImportMany] IEnumerable<Lazy<IBamlDecompiler>> bamlDecompilers,
            [ImportMany] IEnumerable<Lazy<IXamlOutputOptionsProvider>> xamlOutputOptionsProviders)
        {
            this.documentTreeView = documentTreeView;
            this.documentService = documentService;
            this.dbgManager = dbgManager;
            this.documentTabService = documentTabService;
            this.decompilerService = decompilerService;
            bamlDecompiler = bamlDecompilers.FirstOrDefault();
            xamlOutputOptionsProvider = xamlOutputOptionsProviders.FirstOrDefault();
        }

        public CallToolResult ListAssemblies()
        {
            var assemblies = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Where(m => m.Document?.AssemblyDef != null)
                    // Group by the IDsDocument so multi-module assemblies appear once
                    .GroupBy(m => m.Document)
                    .Select(g =>
                    {
                        var doc = g.Key!;
                        var a = doc.AssemblyDef!;
                        return new
                        {
                            Name = a.Name.String,
                            Version = a.Version?.ToString() ?? "N/A",
                            FullName = a.FullName,
                            Culture = string.IsNullOrEmpty(a.Culture) ? "neutral" : a.Culture.String,
                            PublicKeyToken = a.PublicKeyToken?.ToString() ?? "null",
                            FilePath = doc.Filename ?? ""
                        };
                    })
                    .ToList());

            var result = JsonSerializer.Serialize(assemblies, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> {
                    new ToolContent { Text = result }
                }
            };
        }

        public CallToolResult GetAssemblyInfo(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("assembly_name", out var assemblyNameObj))
                throw new ArgumentException("assembly_name is required");

            var assemblyName = assemblyNameObj.ToString() ?? string.Empty;
            var assembly = FindAssemblyByName(assemblyName);
            if (assembly == null)
                throw new ArgumentException($"Assembly not found: {assemblyName}");

            string? cursor = null;
            if (arguments.TryGetValue("cursor", out var cursorObj))
                cursor = cursorObj.ToString();

            var (offset, pageSize) = DecodeCursor(cursor);

            var modules = assembly.Modules.Select(m => new
            {
                Name = m.Name.String,
                Kind = m.Kind.ToString(),
                Architecture = m.Machine.ToString(),
                RuntimeVersion = m.RuntimeVersion
            }).ToList();

            var allNamespaces = assembly.Modules
                .SelectMany(m => GetAllTypesRecursive(m.Types))
                .Select(t => t.Namespace.String)
                .Distinct()
                .OrderBy(ns => ns)
                .ToList();

            var namespacesToReturn = allNamespaces.Skip(offset).Take(pageSize).ToList();
            var hasMore = offset + pageSize < allNamespaces.Count;

            var info = new Dictionary<string, object>
            {
                ["Name"] = assembly.Name.String,
                ["Version"] = assembly.Version?.ToString() ?? "N/A",
                ["FullName"] = assembly.FullName,
                ["Culture"] = string.IsNullOrEmpty(assembly.Culture) ? "neutral" : assembly.Culture.String,
                ["PublicKeyToken"] = assembly.PublicKeyToken?.ToString() ?? "null",
                ["Modules"] = modules,
                ["Namespaces"] = namespacesToReturn,
                ["NamespacesTotalCount"] = allNamespaces.Count,
                ["NamespacesReturnedCount"] = namespacesToReturn.Count,
                ["TypeCount"] = assembly.Modules.Sum(m => GetAllTypesRecursive(m.Types).Count())
            };

            if (hasMore)
            {
                info["nextCursor"] = EncodeCursor(offset + pageSize, pageSize);
            }

            var result = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> {
                    new ToolContent { Text = result }
                }
            };
        }

        public CallToolResult GetPeInfo(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault()
                ?? throw new InvalidOperationException($"Assembly '{assembly.Name.String}' has no manifest module.");

            var targetFramework = TryGetTargetFramework(assembly);
            var cor20Flags = module is ModuleDefMD moduleDefMd
                ? moduleDefMd.Metadata?.ImageCor20Header?.Flags.ToString()
                : null;

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["assembly_full_name"] = assembly.FullName,
                ["module_name"] = module.Name,
                ["file_path"] = module.Location,
                ["machine"] = module.Machine.ToString(),
                ["characteristics"] = module.Characteristics.ToString(),
                ["kind"] = module.Kind.ToString(),
                ["runtime_version"] = module.RuntimeVersion,
                ["mvid"] = (module.Mvid ?? Guid.Empty).ToString("N"),
                ["is_manifest_module"] = module.IsManifestModule,
                ["is_32bit_required"] = module.Is32BitRequired,
                ["is_32bit_preferred"] = module.Is32BitPreferred,
                ["is_il_only"] = module.IsILOnly,
                ["cor20_flags"] = cor20Flags,
                ["target_framework"] = targetFramework,
                ["entry_point"] = module.EntryPoint?.FullName,
                ["native_entry_point"] = module.NativeEntryPoint,
                ["has_native_entry_point"] = module.NativeEntryPoint != 0,
                ["module_count"] = assembly.Modules.Count,
                ["type_count"] = assembly.Modules.Sum(m => GetAllTypesRecursive(m.Types).Count()),
                ["public_key_token"] = assembly.PublicKeyToken?.ToString() ?? "null"
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult ValidateAssembly(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = ResolveMetadataModule(arguments);
            var metadata = module.Metadata;
            var tables = metadata.TablesStream;
            var level = arguments.TryGetValue("level", out var levelObj) && !string.IsNullOrWhiteSpace(levelObj?.ToString())
                ? levelObj!.ToString()!.Trim().ToLowerInvariant()
                : "production";

            if (level != "minimal" && level != "production" && level != "strict")
                throw new ArgumentException("level must be one of: minimal, production, strict");

            var issues = new List<string>();
            var warnings = new List<string>();
            var checks = new List<Dictionary<string, object?>>();

            void AddCheck(string name, bool passed, string? detail = null)
            {
                checks.Add(new Dictionary<string, object?> {
                    ["name"] = name,
                    ["passed"] = passed,
                    ["detail"] = detail
                });

                if (!passed && detail != null)
                    issues.Add($"{name}: {detail}");
            }

            AddCheck("manifest_module_present", assembly.ManifestModule != null, assembly.ManifestModule == null ? "Assembly has no manifest module." : null);
            AddCheck("metadata_present", metadata != null, metadata == null ? "Module does not expose dnlib metadata." : null);
            AddCheck("module_table_present", tables.TryReadModuleRow(1, out _), "Module row 1 could not be read.");

            if (module.IsManifestModule && tables.AssemblyTable.Rows == 0)
                warnings.Add("Manifest module has no Assembly table rows.");

            if (metadata != null)
            {
                AddCheck("strings_heap_bounds", metadata.StringsStream.EndOffset >= metadata.StringsStream.StartOffset, "Invalid #Strings heap bounds.");
                AddCheck("blob_heap_bounds", metadata.BlobStream.EndOffset >= metadata.BlobStream.StartOffset, "Invalid #Blob heap bounds.");
                AddCheck("guid_heap_bounds", metadata.GuidStream.EndOffset >= metadata.GuidStream.StartOffset, "Invalid #GUID heap bounds.");
                AddCheck("userstrings_heap_bounds", metadata.USStream.EndOffset >= metadata.USStream.StartOffset, "Invalid #US heap bounds.");
            }

            if (module.EntryPoint != null)
            {
                bool entryPointResolved;
                try
                {
                    entryPointResolved = module.ResolveToken(module.EntryPoint.MDToken.Raw) != null;
                }
                catch
                {
                    entryPointResolved = false;
                }

                AddCheck("entry_point_resolvable", entryPointResolved, entryPointResolved ? null : "Entry point token could not be resolved.");
            }

            var tableChecks = new List<Dictionary<string, object?>>();
            tableChecks.Add(BuildTableValidationResult("TypeDef", tables.TypeDefTable.Rows, rid => tables.TryReadTypeDefRow(rid, out _)));
            tableChecks.Add(BuildTableValidationResult("Method", tables.MethodTable.Rows, rid => tables.TryReadMethodRow(rid, out _)));
            tableChecks.Add(BuildTableValidationResult("Field", tables.FieldTable.Rows, rid => tables.TryReadFieldRow(rid, out _)));

            if (level != "minimal")
            {
                tableChecks.Add(BuildTableValidationResult("TypeRef", tables.TypeRefTable.Rows, rid => tables.TryReadTypeRefRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("MemberRef", tables.MemberRefTable.Rows, rid => tables.TryReadMemberRefRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("Param", tables.ParamTable.Rows, rid => tables.TryReadParamRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("ManifestResource", tables.ManifestResourceTable.Rows, rid => tables.TryReadManifestResourceRow(rid, out _)));
            }

            if (level == "strict")
            {
                tableChecks.Add(BuildTableValidationResult("InterfaceImpl", tables.InterfaceImplTable.Rows, rid => tables.TryReadInterfaceImplRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("Property", tables.PropertyTable.Rows, rid => tables.TryReadPropertyRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("Event", tables.EventTable.Rows, rid => tables.TryReadEventRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("AssemblyRef", tables.AssemblyRefTable.Rows, rid => tables.TryReadAssemblyRefRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("ModuleRef", tables.ModuleRefTable.Rows, rid => tables.TryReadModuleRefRow(rid, out _)));
                tableChecks.Add(BuildTableValidationResult("ImplMap", tables.ImplMapTable.Rows, rid => tables.TryReadImplMapRow(rid, out _)));
            }

            foreach (var tableCheck in tableChecks)
            {
                var failedRows = tableCheck.TryGetValue("failed_rows", out var failedRowsObj) ? Convert.ToInt32(failedRowsObj) : 0;
                if (failedRows > 0)
                    issues.Add($"{tableCheck["table_name"]}: {failedRows} unreadable row(s).");
            }

            var status = issues.Count == 0
                ? (warnings.Count == 0 ? "pass" : "pass_with_warnings")
                : "fail";

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["module_name"] = module.Name,
                ["file_path"] = module.Location,
                ["level"] = level,
                ["status"] = status,
                ["checks"] = checks,
                ["table_checks"] = tableChecks,
                ["warnings"] = warnings,
                ["issues"] = issues,
                ["warning_count"] = warnings.Count,
                ["issue_count"] = issues.Count
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult ListMetadataTables(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var module = ResolveMetadataModule(arguments);
            var metadata = module.Metadata;
            var filter = arguments.TryGetValue("table", out var tableObj) ? tableObj?.ToString() : null;
            var includeEmpty = ReadBoolean(arguments, "include_empty", defaultValue: false);

            var items = metadata.TablesStream.MDTables
                .Where(a => includeEmpty || a.Rows > 0)
                .Where(a => string.IsNullOrWhiteSpace(filter) || a.Table.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(a => new Dictionary<string, object?> {
                    ["table_name"] = a.Table.ToString(),
                    ["table_id"] = (int)a.Table,
                    ["row_count"] = a.Rows,
                    ["row_size"] = a.RowSize,
                    ["start_offset"] = $"0x{(uint)a.StartOffset:X8}",
                    ["end_offset"] = $"0x{(uint)a.EndOffset:X8}"
                })
                .OrderBy(a => a["table_id"])
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = module.Assembly?.Name.String,
                ["module_name"] = module.Name,
                ["items"] = items,
                ["total_count"] = items.Count,
                ["returned_count"] = items.Count
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult DumpMetadataHeap(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("heap", out var heapObj))
                throw new ArgumentException("heap is required");

            var module = ResolveMetadataModule(arguments);
            var heapName = NormalizeHeapName(heapObj?.ToString());
            var offsetText = arguments.TryGetValue("offset", out var offsetObj) ? offsetObj?.ToString() : null;
            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            if (!string.IsNullOrWhiteSpace(offsetText))
            {
                var heapOffset = ParseUnsignedInteger(offsetText!);
                var payload = DescribeMetadataHeapEntry(module, heapName, heapOffset);
                payload["heap"] = heapName;
                payload["assembly_name"] = module.Assembly?.Name.String;
                payload["module_name"] = module.Name;

                var singleJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                return new CallToolResult {
                    Content = new List<ToolContent> { new ToolContent { Text = singleJson } }
                };
            }

            var items = EnumerateMetadataHeapEntries(module, heapName).ToList();
            var page = items.Skip(offset).Take(pageSize).ToList();
            var payloadPage = new Dictionary<string, object?> {
                ["assembly_name"] = module.Assembly?.Name.String,
                ["module_name"] = module.Name,
                ["heap"] = heapName,
                ["items"] = page,
                ["total_count"] = items.Count,
                ["returned_count"] = page.Count,
                ["has_more"] = offset + pageSize < items.Count
            };

            if (offset + pageSize < items.Count)
                payloadPage["next_cursor"] = EncodeCursor(offset + pageSize, pageSize);

            var json = JsonSerializer.Serialize(payloadPage, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult NormalizeMemberId(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Reference arguments are required.");

            var resolved = ResolveReference(arguments);
            var payload = DescribeReferenceWithMemberId(resolved);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult ResolveMemberId(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("member_id", out var memberIdObj))
                throw new ArgumentException("member_id is required");

            var memberId = memberIdObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(memberId))
                throw new ArgumentException("member_id cannot be empty");

            var resolved = ResolveMemberIdCore(memberId);
            var payload = DescribeReferenceWithMemberId(resolved);
            payload["member_id"] = memberId;

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult GetMemberDetails(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            var payload = DescribeReferenceWithMemberId(resolved);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult GetDecompiledSource(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            var payload = DescribeReferenceWithMemberId(resolved);
            payload["source"] = DecompileResolvedReference(resolved);
            payload["decompile_scope"] = GetDecompileScope(resolved);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult BatchGetDecompiledSource(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("member_ids", out var memberIdsObj))
                throw new ArgumentException("member_ids is required");

            var memberIds = ReadStringArray(memberIdsObj);
            if (memberIds.Count == 0)
                throw new ArgumentException("member_ids must contain at least one member_id");

            var items = new List<Dictionary<string, object?>>();
            foreach (var memberId in memberIds)
            {
                try
                {
                    var resolved = ResolveMemberIdCore(memberId);
                    var item = DescribeReferenceWithMemberId(resolved);
                    item["source"] = DecompileResolvedReference(resolved);
                    item["decompile_scope"] = GetDecompileScope(resolved);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    items.Add(new Dictionary<string, object?> {
                        ["member_id"] = memberId,
                        ["error"] = ex.Message
                    });
                }
            }

            var payload = new Dictionary<string, object?> {
                ["items"] = items,
                ["requested_count"] = memberIds.Count,
                ["returned_count"] = items.Count
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult GetAstOutline(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id, symbol reference arguments, or source is required");

            var includeInvocations = ReadBoolean(arguments, "include_invocations", false);
            string source;
            string? memberId = null;
            string sourceOrigin;
            string? sourceName = null;

            if (arguments.TryGetValue("source", out var sourceObj) && !string.IsNullOrWhiteSpace(sourceObj?.ToString()))
            {
                source = sourceObj!.ToString()!;
                sourceOrigin = "inline_source";
                sourceName = arguments.TryGetValue("source_name", out var sourceNameObj) ? sourceNameObj?.ToString() : null;
            }
            else
            {
                object resolved;
                if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                {
                    memberId = memberIdObj!.ToString()!;
                    resolved = ResolveMemberIdCore(memberId);
                }
                else
                {
                    resolved = ResolveReference(arguments);
                }

                var described = DescribeReferenceWithMemberId(resolved);
                if (described.TryGetValue("member_id", out var describedMemberId))
                    memberId = describedMemberId?.ToString();
                source = DecompileResolvedReference(resolved);
                sourceOrigin = GetDecompileScope(resolved);
                sourceName = described.TryGetValue("display_name", out var displayName)
                    ? displayName?.ToString()
                    : described.TryGetValue("type_full_name", out var typeName) ? typeName?.ToString() : null;
            }

            var cacheKey = BuildAstOutlineCacheKey(memberId, source, includeInvocations);
            if (TryGetCachedAstOutline(cacheKey, out var cachedPayload))
            {
                var cachedCopy = new Dictionary<string, object?>(cachedPayload, StringComparer.Ordinal) {
                    ["cache_hit"] = true
                };
                if (!string.IsNullOrWhiteSpace(sourceName))
                    cachedCopy["source_name"] = sourceName;
                return ToolResponseFactory.Json(cachedCopy);
            }

            var payload = BuildAstOutlinePayload(source, includeInvocations, memberId, sourceOrigin, sourceName);
            payload["cache_hit"] = false;
            AddAstOutlineCacheEntry(cacheKey, payload);
            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult DecompileAssembly(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var namespaceFilter = arguments.TryGetValue("namespace", out var nsObj) ? nsObj?.ToString() : null;
            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var types = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .Where(a => !a.IsGlobalModuleType)
                .Where(a => string.IsNullOrWhiteSpace(namespaceFilter) ||
                            string.Equals(a.Namespace.String, namespaceFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.FullName, StringComparer.Ordinal)
                .ToList();

            var page = types.Skip(offset).Take(pageSize).ToList();
            var items = page.Select(a => new Dictionary<string, object?> {
                ["type_full_name"] = a.FullName,
                ["namespace"] = a.Namespace.String,
                ["member_id"] = $"{(a.Module.Mvid ?? Guid.Empty):N}:{a.MDToken.Raw:X8}:T",
                ["source"] = DecompileType(a)
            }).ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["items"] = items,
                ["total_count"] = types.Count,
                ["returned_count"] = items.Count,
                ["has_more"] = offset + pageSize < types.Count
            };

            if (offset + pageSize < types.Count)
                payload["next_cursor"] = EncodeCursor(offset + pageSize, pageSize);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult ExportToProject(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path plus output_dir are required");
            if (!arguments.TryGetValue("output_dir", out var outputDirObj) || string.IsNullOrWhiteSpace(outputDirObj?.ToString()))
                throw new ArgumentException("output_dir is required");

            var assembly = ResolveAssembly(arguments);
            var outputDir = Path.GetFullPath(outputDirObj!.ToString()!);
            bool includeResources = ReadBoolean(arguments, "include_resources", true);
            bool includeXaml = ReadBoolean(arguments, "include_xaml", true);

            Directory.CreateDirectory(outputDir);

            var sourceRoot = Path.Combine(outputDir, "src");
            Directory.CreateDirectory(sourceRoot);

            var writtenFiles = new List<string>();
            var types = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .Where(a => !a.IsGlobalModuleType)
                .OrderBy(a => a.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var type in types)
            {
                var relativeName = BuildProjectTypeRelativePath(type);
                var filePath = Path.Combine(sourceRoot, relativeName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, DecompileType(type), Encoding.UTF8);
                writtenFiles.Add(filePath);
            }

            var exportedResources = new List<string>();
            if (includeResources)
            {
                foreach (var resource in assembly.ManifestModule?.Resources ?? Enumerable.Empty<Resource>())
                {
                    if (resource is not EmbeddedResource embedded)
                        continue;

                    var resourcePath = Path.Combine(outputDir, "resources", SanitizePathSegment(embedded.Name.String));
                    Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
                    File.WriteAllBytes(resourcePath, embedded.CreateReader().ToArray());
                    exportedResources.Add(resourcePath);
                }

                if (includeXaml)
                {
                    var xamlOutputDir = Path.Combine(outputDir, "xaml");
                    Directory.CreateDirectory(xamlOutputDir);
                    foreach (var item in EnumerateBamlElements(assembly))
                    {
                        var xamlText = DecompileBamlBytes(item.Module, item.Data, out var refs, out var typeName);
                        var baseName = Path.GetFileNameWithoutExtension(item.Element.Name);
                        var relativeName = string.IsNullOrWhiteSpace(baseName) ? "view" : baseName;
                        var xamlPath = Path.Combine(xamlOutputDir, $"{SanitizePathSegment(relativeName)}.xaml");
                        File.WriteAllText(xamlPath, xamlText, Encoding.UTF8);
                        exportedResources.Add(xamlPath);

                        var refsPath = Path.ChangeExtension(xamlPath, ".refs.json");
                        File.WriteAllText(refsPath, JsonSerializer.Serialize(new {
                            resource_name = item.ResourceName,
                            element_name = item.Element.Name,
                            type_name = typeName,
                            assembly_references = refs
                        }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                        exportedResources.Add(refsPath);
                    }
                }
            }

            var projectFile = Path.Combine(outputDir, $"{SanitizePathSegment(assembly.Name.String)}.csproj");
            File.WriteAllText(projectFile, BuildSdkProjectText(assembly, includeResources), Encoding.UTF8);
            writtenFiles.Add(projectFile);

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["output_dir"] = outputDir,
                ["project_file"] = projectFile,
                ["type_count"] = types.Count,
                ["source_file_count"] = writtenFiles.Count - 1,
                ["resource_file_count"] = exportedResources.Count,
                ["files_written"] = writtenFiles.Concat(exportedResources).ToList()
            });
        }

        public CallToolResult DecompileBaml(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var resourceName = arguments.TryGetValue("resource_name", out var resourceNameObj) ? resourceNameObj?.ToString() : null;
            var elementName = arguments.TryGetValue("element_name", out var elementNameObj) ? elementNameObj?.ToString() : null;
            var match = EnumerateBamlElements(assembly, resourceName, elementName).FirstOrDefault();
            if (match.Element == null)
                throw new ArgumentException("No matching BAML resource element was found. Provide resource_name and/or element_name.");

            var xaml = DecompileBamlBytes(match.Module, match.Data, out var refs, out var typeName);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["resource_name"] = match.ResourceName,
                ["element_name"] = match.Element.Name,
                ["type_name"] = typeName,
                ["assembly_references"] = refs,
                ["xaml"] = xaml
            });
        }

        public CallToolResult ExportXamlResources(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path plus output_dir are required");
            if (!arguments.TryGetValue("output_dir", out var outputDirObj) || string.IsNullOrWhiteSpace(outputDirObj?.ToString()))
                throw new ArgumentException("output_dir is required");

            var assembly = ResolveAssembly(arguments);
            var outputDir = Path.GetFullPath(outputDirObj!.ToString()!);
            Directory.CreateDirectory(outputDir);

            var files = new List<Dictionary<string, object?>>();
            foreach (var item in EnumerateBamlElements(assembly))
            {
                var xaml = DecompileBamlBytes(item.Module, item.Data, out var refs, out var typeName);
                var baseName = Path.GetFileNameWithoutExtension(item.Element.Name);
                var path = Path.Combine(outputDir, $"{SanitizePathSegment(baseName)}.xaml");
                File.WriteAllText(path, xaml, Encoding.UTF8);

                files.Add(new Dictionary<string, object?> {
                    ["resource_name"] = item.ResourceName,
                    ["element_name"] = item.Element.Name,
                    ["type_name"] = typeName,
                    ["assembly_references"] = refs,
                    ["path"] = path
                });
            }

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["output_dir"] = outputDir,
                ["count"] = files.Count,
                ["items"] = files
            });
        }

        public CallToolResult ListResourceElements(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var resourceFilter = arguments.TryGetValue("resource_name", out var resourceObj) ? resourceObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = EnumerateResourceElementSets(assembly, resourceFilter)
                .SelectMany(a => a.Set.ResourceElements.Select(b => DescribeResourceElement(a.Module, a.Resource, b)))
                .OrderBy(a => a["resource_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["element_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult ExtractResxBundle(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path plus output_dir are required");
            if (!arguments.TryGetValue("output_dir", out var outputDirObj) || string.IsNullOrWhiteSpace(outputDirObj?.ToString()))
                throw new ArgumentException("output_dir is required");

            var assembly = ResolveAssembly(arguments);
            var outputDir = Path.GetFullPath(outputDirObj!.ToString()!);
            Directory.CreateDirectory(outputDir);

            var files = new List<Dictionary<string, object?>>();
            foreach (var entry in EnumerateResourceElementSets(assembly))
            {
                var baseName = Path.GetFileNameWithoutExtension(entry.Resource.Name.String);
                var resxPath = Path.Combine(outputDir, $"{SanitizePathSegment(baseName)}.resx");
                WriteSimpleResxFile(entry.Set, resxPath);
                files.Add(new Dictionary<string, object?> {
                    ["resource_name"] = entry.Resource.Name.String,
                    ["path"] = resxPath,
                    ["element_count"] = entry.Set.ResourceElements.Count()
                });
            }

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["output_dir"] = outputDir,
                ["count"] = files.Count,
                ["items"] = files
            });
        }

        public CallToolResult GetSsa(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M",
                ["method_name"] = method.FullName,
                ["ssa"] = BuildPseudoSsa(method)
            });
        }

        public CallToolResult EmulateMethod(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            int maxSteps = arguments != null && arguments.TryGetValue("max_steps", out var maxStepsObj) ? ReadIntValue(maxStepsObj, 256) : 256;
            var trace = EmulateMethodCore(method, maxSteps);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M",
                ["method_name"] = method.FullName,
                ["max_steps"] = maxSteps,
                ["trace"] = trace
            });
        }

        public CallToolResult GetStartupMap(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault()
                ?? throw new InvalidOperationException($"Assembly '{assembly.Name.String}' has no modules.");

            var allTypes = assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)).ToList();
            var staticConstructors = allTypes
                .SelectMany(a => a.Methods)
                .Where(a => a.IsStaticConstructor)
                .Select(a => new Dictionary<string, object?> {
                    ["type_full_name"] = a.DeclaringType?.FullName,
                    ["method_name"] = a.Name.String,
                    ["member_id"] = $"{(a.Module.Mvid ?? Guid.Empty):N}:{a.MDToken.Raw:X8}:M"
                })
                .ToList();

            var moduleInitializers = allTypes
                .SelectMany(a => a.Methods)
                .Where(HasModuleInitializerAttribute)
                .Select(a => new Dictionary<string, object?> {
                    ["type_full_name"] = a.DeclaringType?.FullName,
                    ["method_name"] = a.Name.String,
                    ["member_id"] = $"{(a.Module.Mvid ?? Guid.Empty):N}:{a.MDToken.Raw:X8}:M"
                })
                .ToList();

            var startupCandidates = allTypes
                .SelectMany(a => a.Methods)
                .Where(IsLikelyStartupMethod)
                .Take(50)
                .Select(a => new Dictionary<string, object?> {
                    ["type_full_name"] = a.DeclaringType?.FullName,
                    ["method_name"] = a.Name.String,
                    ["signature"] = a.MethodSig?.ToString(),
                    ["member_id"] = $"{(a.Module.Mvid ?? Guid.Empty):N}:{a.MDToken.Raw:X8}:M"
                })
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["entry_point"] = DescribeEntryPoint(module.EntryPoint),
                ["has_native_entry_point"] = module.NativeEntryPoint != 0,
                ["native_entry_point"] = module.NativeEntryPoint != 0 ? $"0x{module.NativeEntryPoint:X8}" : null,
                ["module_initializers"] = moduleInitializers,
                ["module_initializer_count"] = moduleInitializers.Count,
                ["static_constructors"] = staticConstructors,
                ["static_constructor_count"] = staticConstructors.Count,
                ["startup_candidates"] = startupCandidates,
                ["startup_candidate_count"] = startupCandidates.Count
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult AnalyzeStaticConstructors(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(a => a.Modules.SelectMany(b => GetAllTypesRecursive(b.Types)))
                .Select(a => new { Type = a, StaticCtor = a.Methods.FirstOrDefault(b => b.IsStaticConstructor) })
                .Where(a => a.StaticCtor != null)
                .Select(a => {
                    var method = a.StaticCtor!;
                    var stringLiterals = method.Body?.Instructions?
                        .Where(b => b.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && b.Operand is string)
                        .Select(b => b.Operand!.ToString()!)
                        .Distinct(StringComparer.Ordinal)
                        .Take(10)
                        .ToList() ?? new List<string>();

                    return new Dictionary<string, object?> {
                        ["assembly_name"] = method.Module.Assembly?.Name.String,
                        ["type_full_name"] = a.Type.FullName,
                        ["type_member_id"] = $"{(a.Type.Module.Mvid ?? Guid.Empty):N}:{a.Type.MDToken.Raw:X8}:T",
                        ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M",
                        ["method_name"] = method.Name.String,
                        ["before_field_init"] = a.Type.IsBeforeFieldInit,
                        ["instruction_count"] = method.Body?.Instructions?.Count ?? 0,
                        ["called_methods"] = EnumerateCalledMethods(method)
                            .Select(DescribeMethodLikeReference)
                            .Distinct(DictionaryByJsonComparer.Instance)
                            .Take(10)
                            .ToList(),
                        ["string_literals"] = stringLiterals,
                        ["source"] = DecompileMethod(method)
                    };
                })
                .OrderBy(a => a["type_full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult GetResourceMap(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault()
                ?? throw new InvalidOperationException($"Assembly '{assembly.Name.String}' has no modules.");

            var items = module.Resources.Select((a, index) => {
                long? size = null;
                if (a is EmbeddedResource embedded)
                {
                    try
                    {
                        size = embedded.CreateReader().Length;
                    }
                    catch
                    {
                    }
                }

                return new Dictionary<string, object?> {
                    ["index"] = index,
                    ["name"] = a.Name.String,
                    ["kind"] = a.ResourceType.ToString(),
                    ["is_public"] = a.IsPublic,
                    ["size_bytes"] = size,
                    ["classification"] = ClassifyResource(a.Name.String),
                    ["is_costura_embedded"] = a.Name.String.StartsWith("costura.", StringComparison.OrdinalIgnoreCase)
                };
            }).ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["resource_count"] = items.Count,
                ["items"] = items,
                ["contains_baml"] = items.Any(a => string.Equals(a["classification"]?.ToString(), "baml", StringComparison.OrdinalIgnoreCase)),
                ["contains_xaml"] = items.Any(a => string.Equals(a["classification"]?.ToString(), "xaml", StringComparison.OrdinalIgnoreCase)),
                ["contains_embedded_assemblies"] = items.Any(a => string.Equals(a["classification"]?.ToString(), "assembly", StringComparison.OrdinalIgnoreCase)),
                ["contains_images"] = items.Any(a => string.Equals(a["classification"]?.ToString(), "image", StringComparison.OrdinalIgnoreCase))
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult GetReconstructionDiagnostics(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault()
                ?? throw new InvalidOperationException($"Assembly '{assembly.Name.String}' has no modules.");
            var allTypes = assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)).ToList();
            var staticCtorCount = allTypes.SelectMany(a => a.Methods).Count(a => a.IsStaticConstructor);
            var resourceCount = module.Resources.Count;
            var references = module.GetAssemblyRefs().ToList();
            var namespaceCounts = allTypes.GroupBy(a => a.Namespace.String).OrderByDescending(a => a.Count()).Take(15)
                .Select(a => new Dictionary<string, object?> {
                    ["namespace"] = a.Key,
                    ["type_count"] = a.Count()
                }).ToList();

            var interestingDependencies = references
                .Select(a => a.Name.String)
                .Where(a => a.StartsWith("System.IO", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("System.Xml", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("System.Drawing", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("Microsoft.Xna", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["target_framework"] = TryGetTargetFramework(assembly),
                ["entry_point"] = DescribeEntryPoint(module.EntryPoint),
                ["type_count"] = allTypes.Count,
                ["namespace_count"] = allTypes.Select(a => a.Namespace.String).Distinct(StringComparer.Ordinal).Count(),
                ["resource_count"] = resourceCount,
                ["assembly_reference_count"] = references.Count,
                ["static_constructor_count"] = staticCtorCount,
                ["has_wpf_resources"] = module.Resources.Any(a => a.Name.String.EndsWith(".baml", StringComparison.OrdinalIgnoreCase) ||
                                                                  a.Name.String.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)),
                ["has_embedded_assemblies"] = module.Resources.Any(a => IsAssemblyLikeResource(a.Name.String)),
                ["interesting_dependencies"] = interestingDependencies,
                ["largest_namespaces"] = namespaceCounts
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        public CallToolResult SearchMembers(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("query", out var queryObj))
                throw new ArgumentException("query is required");

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query cannot be empty");

            var kindFilter = arguments.TryGetValue("kind", out var kindObj) ? kindObj?.ToString()?.ToLowerInvariant() : null;
            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);
            var regex = BuildPatternRegex(query);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(a => SearchMembersInAssembly(a, regex, kindFilter))
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult SearchStringLiterals(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("query", out var queryObj))
                throw new ArgumentException("query is required");

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query cannot be empty");

            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);
            var regex = BuildPatternRegex(query);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateStringLiteralOccurrences)
                .Where(a => regex.IsMatch(a.Value))
                .Select(a => new Dictionary<string, object?> {
                    ["assembly_name"] = a.Method.Module.Assembly?.Name.String,
                    ["type_full_name"] = a.Method.DeclaringType?.FullName,
                    ["method_name"] = a.Method.Name.String,
                    ["member_id"] = $"{(a.Method.Module.Mvid ?? Guid.Empty):N}:{a.Method.MDToken.Raw:X8}:M",
                    ["string_value"] = a.Value
                })
                .OrderBy(a => a["string_value"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult GetUserStrings(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateStringLiteralOccurrences)
                .GroupBy(a => a.Value, StringComparer.Ordinal)
                .Select(a => new Dictionary<string, object?> {
                    ["string_value"] = a.Key,
                    ["occurrence_count"] = a.Count(),
                    ["assemblies"] = a.Select(b => b.Method.Module.Assembly?.Name.String)
                        .Where(b => !string.IsNullOrWhiteSpace(b))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ["sample_member_ids"] = a.Take(5)
                        .Select(b => $"{(b.Method.Module.Mvid ?? Guid.Empty):N}:{b.Method.MDToken.Raw:X8}:M")
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                })
                .OrderBy(a => a["string_value"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindStringReferences(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("string_value", out var valueObj))
                throw new ArgumentException("string_value is required");

            var stringValue = valueObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(stringValue))
                throw new ArgumentException("string_value cannot be empty");

            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateStringLiteralOccurrences)
                .Where(a => string.Equals(a.Value, stringValue, StringComparison.Ordinal))
                .Select(a => new Dictionary<string, object?> {
                    ["assembly_name"] = a.Method.Module.Assembly?.Name.String,
                    ["type_full_name"] = a.Method.DeclaringType?.FullName,
                    ["method_name"] = a.Method.Name.String,
                    ["member_id"] = $"{(a.Method.Module.Mvid ?? Guid.Empty):N}:{a.Method.MDToken.Raw:X8}:M",
                    ["string_value"] = a.Value
                })
                .OrderBy(a => a["type_full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["method_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindReflectionUsage(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(a => a.Modules.SelectMany(b => GetAllTypesRecursive(b.Types)))
                .SelectMany(a => a.Methods)
                .Where(a => a.Body?.Instructions != null)
                .SelectMany(method =>
                    EnumerateCalledMethods(method)
                        .Where(IsReflectionLikeMethod)
                        .Select(calledMethod => new Dictionary<string, object?> {
                            ["assembly_name"] = method.Module.Assembly?.Name.String,
                            ["type_full_name"] = method.DeclaringType?.FullName,
                            ["method_name"] = method.Name.String,
                            ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M",
                            ["target_api"] = calledMethod.FullName,
                            ["target_declaring_type"] = calledMethod.DeclaringType?.FullName,
                            ["usage_kind"] = ClassifyReflectionUsage(calledMethod)
                        }))
                .Distinct(DictionaryByJsonComparer.Instance)
                .OrderBy(a => a["type_full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["method_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult SuggestSymbolRenames(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            List<Dictionary<string, object?>> suggestions = resolved switch
            {
                MethodDef method => BuildMethodRenameSuggestions(method),
                TypeDef type => BuildTypeRenameSuggestions(type),
                _ => throw new ArgumentException($"suggest_symbol_renames supports methods and types, not '{resolved.GetType().Name}'.")
            };

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(resolved),
                ["suggestions"] = suggestions,
                ["suggestion_count"] = suggestions.Count,
                ["note"] = "Suggestions are heuristic and should be validated before patchback renaming."
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult DetectAntiDebug(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeAntiDebugHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderBy(a => a["confidence"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult DetectAntiTamper(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeAntiTamperHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderBy(a => a["confidence"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindProxyMethods(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeProxyMethodHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult DetectStringEncryption(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeStringDecryptorHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderByDescending(a => a["caller_count"])
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindDelegateCreation(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeDelegateCreationHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindDynamicCode(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Select(DescribeDynamicCodeHit)
                .Where(a => a != null)
                .Cast<Dictionary<string, object?>>()
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindByteArrays(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(DescribeByteArrayHits)
                .OrderBy(a => a["full_name"]?.ToString() ?? a["name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult FindEmbeddedPes(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(DescribeEmbeddedPeHits)
                .OrderBy(a => a["resource_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult AnalyzeControlFlow(Dictionary<string, object>? arguments)
        {
            var cursor = arguments != null && arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(EnumerateAllMethods)
                .Where(a => a.Body?.Instructions != null)
                .Select(DescribeControlFlowSummary)
                .OrderByDescending(a => a["branch_count"])
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult GetCfg(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            if (method.Body?.Instructions == null || method.Body.Instructions.Count == 0)
                throw new ArgumentException("Resolved method does not have an IL body.");

            bool includeInstructions = arguments != null && ReadBoolean(arguments, "include_instructions", false);
            var cfg = BuildControlFlowGraph(method, includeInstructions);
            return ToolResponseFactory.Json(cfg);
        }

        public CallToolResult GetProtectionReport(Dictionary<string, object>? arguments)
        {
            var assemblies = ResolveAssembliesForSearch(arguments).ToList();
            var allMethods = assemblies.SelectMany(EnumerateAllMethods).ToList();

            var antiDebug = allMethods.Select(DescribeAntiDebugHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var antiTamper = allMethods.Select(DescribeAntiTamperHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var proxyMethods = allMethods.Select(DescribeProxyMethodHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var stringDecryptors = allMethods.Select(DescribeStringDecryptorHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var delegateCreators = allMethods.Select(DescribeDelegateCreationHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var dynamicCode = allMethods.Select(DescribeDynamicCodeHit).Where(a => a != null).Cast<Dictionary<string, object?>>().ToList();
            var embeddedPes = assemblies.SelectMany(DescribeEmbeddedPeHits).ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_count"] = assemblies.Count,
                ["assemblies"] = assemblies.Select(a => a.Name.String).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList(),
                ["summary"] = new Dictionary<string, object?> {
                    ["anti_debug_count"] = antiDebug.Count,
                    ["anti_tamper_count"] = antiTamper.Count,
                    ["proxy_method_count"] = proxyMethods.Count,
                    ["string_decryptor_count"] = stringDecryptors.Count,
                    ["delegate_creation_count"] = delegateCreators.Count,
                    ["dynamic_code_count"] = dynamicCode.Count,
                    ["embedded_pe_count"] = embeddedPes.Count
                },
                ["anti_debug"] = antiDebug.Take(20).ToList(),
                ["anti_tamper"] = antiTamper.Take(20).ToList(),
                ["proxy_methods"] = proxyMethods.Take(20).ToList(),
                ["string_decryptors"] = stringDecryptors.Take(20).ToList(),
                ["delegate_creators"] = delegateCreators.Take(20).ToList(),
                ["dynamic_code"] = dynamicCode.Take(20).ToList(),
                ["embedded_pes"] = embeddedPes.Take(20).ToList()
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetManifestAndEntrypoints(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault()
                ?? throw new ArgumentException($"Assembly '{assembly.Name.String}' has no manifest module.");

            var startupCandidates = EnumerateAllMethods(assembly)
                .Where(a => HasModuleInitializerAttribute(a) || IsLikelyStartupMethod(a))
                .Select(a => {
                    var payload = DescribeReferenceWithMemberIdStatic(a);
                    payload["is_module_initializer"] = HasModuleInitializerAttribute(a);
                    payload["is_entry_point"] = module.EntryPoint != null && a.MDToken.Raw == module.EntryPoint.MDToken.Raw;
                    payload["reason"] = payload["is_module_initializer"] as bool? == true ? "module_initializer" : "startup_name_or_signature";
                    return payload;
                })
                .Distinct(DictionaryByJsonComparer.Instance)
                .OrderByDescending(a => a.TryGetValue("is_entry_point", out var entryObj) && entryObj is bool isEntry && isEntry)
                .ThenByDescending(a => a.TryGetValue("is_module_initializer", out var initObj) && initObj is bool isInitializer && isInitializer)
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["assembly_full_name"] = assembly.FullName,
                ["target_framework"] = TryGetTargetFramework(assembly),
                ["manifest_module"] = new Dictionary<string, object?> {
                    ["name"] = module.Name,
                    ["kind"] = module.Kind.ToString(),
                    ["runtime_version"] = module.RuntimeVersion,
                    ["mvid"] = (module.Mvid ?? Guid.Empty).ToString("N"),
                    ["is_manifest_module"] = module.IsManifestModule,
                    ["entry_point"] = module.EntryPoint != null ? DescribeReferenceWithMemberId(module.EntryPoint) : null,
                    ["native_entry_point"] = module.NativeEntryPoint != 0 ? $"0x{module.NativeEntryPoint:X8}" : null,
                    ["exported_type_count"] = module.ExportedTypes.Count,
                    ["resource_count"] = module.Resources.Count
                },
                ["modules"] = assembly.Modules.Select(a => new Dictionary<string, object?> {
                    ["name"] = a.Name,
                    ["kind"] = a.Kind.ToString(),
                    ["runtime_version"] = a.RuntimeVersion,
                    ["mvid"] = (a.Mvid ?? Guid.Empty).ToString("N"),
                    ["has_entry_point"] = a.EntryPoint != null,
                    ["native_entry_point"] = a.NativeEntryPoint != 0 ? $"0x{a.NativeEntryPoint:X8}" : null
                }).ToList(),
                ["startup_candidates"] = startupCandidates,
                ["startup_candidate_count"] = startupCandidates.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetNativeModuleMap(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            string? portableExecutablePath = null;
            Dictionary<string, object?>? exports = null;

            try
            {
                portableExecutablePath = ResolvePortableExecutablePath(arguments);
                exports = ExtractNativeExportsFromPe(portableExecutablePath);
            }
            catch
            {
                // A loaded in-memory assembly may not have a readable file path.
            }

            var imports = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .SelectMany(a => a.Methods)
                .Where(a => a.HasImplMap && a.ImplMap != null)
                .Select(a => new Dictionary<string, object?> {
                    ["managed_method"] = a.FullName,
                    ["member_id"] = $"{(a.Module.Mvid ?? Guid.Empty):N}:{a.MDToken.Raw:X8}:M",
                    ["dll_name"] = a.ImplMap?.Module?.Name?.String ?? string.Empty,
                    ["native_name"] = a.ImplMap?.Name?.String ?? a.Name.String
                })
                .OrderBy(a => a["dll_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["managed_method"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var modules = assembly.Modules.Select(a => new Dictionary<string, object?> {
                ["name"] = a.Name,
                ["kind"] = a.Kind.ToString(),
                ["mvid"] = (a.Mvid ?? Guid.Empty).ToString("N"),
                ["native_entry_point"] = a.NativeEntryPoint != 0 ? $"0x{a.NativeEntryPoint:X8}" : null,
                ["has_native_entry_point"] = a.NativeEntryPoint != 0
            }).ToList();

            var embeddedPes = DescribeEmbeddedPeHits(assembly).ToList();
            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["file_path"] = portableExecutablePath,
                ["modules"] = modules,
                ["imports"] = imports,
                ["import_count"] = imports.Count,
                ["embedded_pes"] = embeddedPes,
                ["embedded_pe_count"] = embeddedPes.Count,
                ["exports"] = exports
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult MatchFrameworkOrPackage(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var matches = BuildFrameworkOrPackageMatches(assembly)
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ThenBy(a => a["name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["items"] = matches,
                ["total_count"] = matches.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult IdentifyKnownBinary(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            return ToolResponseFactory.Json(IdentifyKnownBinaryCore(assembly));
        }

        public CallToolResult LabelThirdPartyComponents(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var deduped = BuildThirdPartyComponentLabels(assembly);

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["items"] = deduped,
                ["total_count"] = deduped.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetProvenanceReport(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var knownBinary = IdentifyKnownBinaryCore(assembly);
            var frameworkMatches = BuildFrameworkOrPackageMatches(assembly)
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ToList();
            var components = BuildThirdPartyComponentLabels(assembly)
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["known_binary"] = knownBinary,
                ["framework_or_package_matches"] = frameworkMatches,
                ["third_party_components"] = components,
                ["summary"] = new Dictionary<string, object?> {
                    ["is_known_binary"] = knownBinary["is_known_binary"],
                    ["framework_match_count"] = frameworkMatches.Count,
                    ["third_party_component_count"] = components.Count
                }
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult LoadSymbols(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var moduleResults = new List<Dictionary<string, object?>>();

            foreach (var module in assembly.Modules) {
                var moduleStatus = DescribeSymbolStatus(module);
                if (module is ModuleDefMD moduleDefMd && module.PdbState == null) {
                    try {
                        moduleDefMd.LoadPdb();
                    }
                    catch (Exception ex) {
                        moduleStatus["load_error"] = ex.Message;
                    }
                }

                moduleStatus["loaded_after_call"] = module.PdbState != null;
                moduleResults.Add(moduleStatus);
            }

            var loadedCount = moduleResults.Count(a => a.TryGetValue("loaded_after_call", out var loadedObj) && loadedObj is bool loaded && loaded);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["module_count"] = moduleResults.Count,
                ["loaded_module_count"] = loadedCount,
                ["items"] = moduleResults
            });
        }

        public CallToolResult GetSymbolStatus(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var moduleStatuses = assembly.Modules.Select(DescribeSymbolStatus).ToList();
            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["has_any_symbols_loaded"] = moduleStatuses.Any(a => a.TryGetValue("symbols_loaded", out var loadedObj) && loadedObj is bool loaded && loaded),
                ["module_count"] = moduleStatuses.Count,
                ["loaded_module_count"] = moduleStatuses.Count(a => a.TryGetValue("symbols_loaded", out var loadedObj) && loadedObj is bool loaded && loaded),
                ["items"] = moduleStatuses
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult FindSourceCandidates(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var candidates = BuildSourceCandidates(assembly)
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ThenBy(a => a.TryGetValue("kind", out var kindObj) ? kindObj?.ToString() : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["total_count"] = candidates.Count,
                ["items"] = candidates
            });
        }

        public CallToolResult MatchOpenSourceCandidates(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var candidates = BuildOpenSourceCandidates(assembly)
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ThenBy(a => a.TryGetValue("repository", out var repositoryObj) ? repositoryObj?.ToString() : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["assembly_name"] = assembly.Name.String,
                ["total_count"] = candidates.Count,
                ["items"] = candidates
            });
        }

        public CallToolResult Triage(Dictionary<string, object>? arguments)
        {
            var assemblies = ResolveAssembliesForSearch(arguments).ToList();
            var allMethods = assemblies.SelectMany(EnumerateAllMethods).ToList();
            var findings = new List<Dictionary<string, object?>>();

            findings.AddRange(allMethods.Select(DescribeAntiDebugHit).Where(a => a != null).Cast<Dictionary<string, object?>>());
            findings.AddRange(allMethods.Select(DescribeAntiTamperHit).Where(a => a != null).Cast<Dictionary<string, object?>>());
            findings.AddRange(allMethods.Select(DescribeStringDecryptorHit).Where(a => a != null).Cast<Dictionary<string, object?>>());
            findings.AddRange(allMethods.Select(DescribeProxyMethodHit).Where(a => a != null).Cast<Dictionary<string, object?>>());
            findings.AddRange(allMethods.Select(DescribeDynamicCodeHit).Where(a => a != null).Cast<Dictionary<string, object?>>());
            findings.AddRange(assemblies.SelectMany(DescribeEmbeddedPeHits));

            var prioritized = findings
                .Select(a => {
                    var clone = new Dictionary<string, object?>(a);
                    clone["priority"] = ComputeTriagePriority(a);
                    return clone;
                })
                .OrderByDescending(a => Convert.ToInt32(a["priority"]))
                .ThenByDescending(a => a.TryGetValue("confidence", out var confObj) ? Convert.ToDouble(confObj) : 0d)
                .Take(50)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["assembly_count"] = assemblies.Count,
                ["finding_count"] = findings.Count,
                ["items"] = prioritized
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetSemanticLabels(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            var labels = new List<Dictionary<string, object?>>();
            if (resolved is MethodDef method)
            {
                foreach (var suggestion in BuildMethodRenameSuggestions(method))
                {
                    labels.Add(new Dictionary<string, object?> {
                        ["label"] = suggestion["role"],
                        ["confidence"] = suggestion["confidence"],
                        ["evidence"] = suggestion["evidence"]
                    });
                }

                if (DescribeAntiDebugHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "anti_debug", ["confidence"] = 0.8 });
                if (DescribeAntiTamperHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "anti_tamper", ["confidence"] = 0.8 });
                if (DescribeStringDecryptorHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "string_decryptor", ["confidence"] = 0.75 });
                if (DescribeProxyMethodHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "proxy_method", ["confidence"] = 0.75 });
                if (DescribeDynamicCodeHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "dynamic_code", ["confidence"] = 0.75 });
                if (DescribeDelegateCreationHit(method) != null)
                    labels.Add(new Dictionary<string, object?> { ["label"] = "delegate_creation", ["confidence"] = 0.7 });
            }
            else if (resolved is TypeDef type)
            {
                foreach (var suggestion in BuildTypeRenameSuggestions(type))
                {
                    labels.Add(new Dictionary<string, object?> {
                        ["label"] = suggestion["role"],
                        ["confidence"] = suggestion["confidence"],
                        ["evidence"] = suggestion["evidence"]
                    });
                }
            }
            else
                throw new ArgumentException("get_semantic_labels supports methods and types.");

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(resolved),
                ["labels"] = labels
                    .GroupBy(a => a["label"]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(a => a.OrderByDescending(b => b.TryGetValue("confidence", out var c) ? Convert.ToDouble(c) : 0d).First())
                    .OrderByDescending(a => a.TryGetValue("confidence", out var c) ? Convert.ToDouble(c) : 0d)
                    .ToList()
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult FindCallees(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            var items = EnumerateCalledMethods(method)
                .Select(DescribeMethodLikeReference)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(method),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetMethodXrefs(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            var callers = FindCallersForMethod(method)
                .Select(DescribeMethodReference)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var callees = EnumerateCalledMethods(method)
                .Select(DescribeMethodLikeReference)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(method),
                ["callers"] = callers,
                ["caller_count"] = callers.Count,
                ["callees"] = callees,
                ["callee_count"] = callees.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult FindBaseTypes(Dictionary<string, object>? arguments)
        {
            var type = ResolveTypeReference(arguments);
            var items = new List<Dictionary<string, object?>>();
            var current = type.BaseType?.ResolveTypeDef();
            while (current != null)
            {
                items.Add(DescribeReferenceWithMemberId(current));
                current = current.BaseType?.ResolveTypeDef();
            }

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(type),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult FindDerivedTypes(Dictionary<string, object>? arguments)
        {
            var type = ResolveTypeReference(arguments);
            var items = FindDerivedTypesCore(type)
                .Select(DescribeReferenceWithMemberId)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(type),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetImplementations(Dictionary<string, object>? arguments)
        {
            var type = ResolveTypeReference(arguments);
            var implementations = new List<TypeDef>();

            foreach (var candidate in EnumerateAllLoadedTypes())
            {
                if (candidate.FullName == type.FullName)
                    continue;

                var implementsTarget = candidate.Interfaces.Any(a => string.Equals(a.Interface?.FullName, type.FullName, StringComparison.Ordinal));
                var derivesTarget = InheritsFrom(candidate, type);
                if (implementsTarget || derivesTarget)
                    implementations.Add(candidate);
            }

            var items = implementations
                .Distinct()
                .Select(DescribeReferenceWithMemberId)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(type),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult GetOverrides(Dictionary<string, object>? arguments)
        {
            var method = ResolveMethodReference(arguments);
            if (method.DeclaringType == null)
                throw new ArgumentException("Resolved method has no declaring type.");

            var items = FindDerivedTypesCore(method.DeclaringType)
                .SelectMany(a => a.Methods)
                .Where(a => a.Name.String.Equals(method.Name.String, StringComparison.Ordinal) &&
                            string.Equals(a.MethodSig?.ToString(), method.MethodSig?.ToString(), StringComparison.Ordinal))
                .Select(DescribeReferenceWithMemberId)
                .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var payload = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(method),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult FindUsages(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            List<Dictionary<string, object?>> items;
            switch (resolved)
            {
                case MethodDef methodDef:
                    items = FindCallersForMethod(methodDef)
                        .Select(a => {
                            var payload = DescribeReferenceWithMemberId(a);
                            payload["usage_kind"] = "call";
                            return payload;
                        })
                        .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                case TypeDef typeDef:
                    items = EnumerateAllLoadedTypes()
                        .Where(a => a.FullName != typeDef.FullName)
                        .SelectMany(a => DescribeTypeUsages(a, typeDef))
                        .OrderBy(a => a["usage_location"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                case FieldDef fieldDef:
                    items = EnumerateAllLoadedTypes()
                        .SelectMany(a => a.Methods)
                        .Where(a => a.Body?.Instructions != null)
                        .SelectMany(a => a.Body!.Instructions
                            .Where(b =>
                                (b.OpCode.Code == dnlib.DotNet.Emit.Code.Ldfld ||
                                 b.OpCode.Code == dnlib.DotNet.Emit.Code.Ldsfld ||
                                 b.OpCode.Code == dnlib.DotNet.Emit.Code.Stfld ||
                                 b.OpCode.Code == dnlib.DotNet.Emit.Code.Stsfld) &&
                                b.Operand is FieldDef usedField &&
                                string.Equals(usedField.FullName, fieldDef.FullName, StringComparison.Ordinal))
                            .Select(b => {
                                var payload = DescribeReferenceWithMemberId(a);
                                payload["usage_kind"] = b.OpCode.Code == dnlib.DotNet.Emit.Code.Ldfld || b.OpCode.Code == dnlib.DotNet.Emit.Code.Ldsfld
                                    ? "field_read"
                                    : "field_write";
                                return payload;
                            }))
                        .Distinct(DictionaryByJsonComparer.Instance)
                        .OrderBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;

                default:
                    throw new ArgumentException($"find_usages does not yet support reference kind '{resolved.GetType().Name}'.");
            }

            var payloadRoot = new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(resolved),
                ["items"] = items,
                ["total_count"] = items.Count
            };

            return ToolResponseFactory.Json(payloadRoot);
        }

        public CallToolResult SearchAttributes(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("query", out var queryObj))
                throw new ArgumentException("query is required");

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query cannot be empty");

            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);
            var regex = BuildPatternRegex(query);

            var items = ResolveAssembliesForSearch(arguments)
                .SelectMany(a => EnumerateAttributeHits(a, regex))
                .OrderBy(a => a["attribute_type"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["owner_full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult ListTypes(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("assembly_name", out var assemblyNameObj))
                throw new ArgumentException("assembly_name is required");

            var assemblyName = assemblyNameObj.ToString() ?? string.Empty;
            var assembly = FindAssemblyByName(assemblyName);
            if (assembly == null)
                throw new ArgumentException($"Assembly not found: {assemblyName}");

            string? namespaceFilter = null;
            if (arguments.TryGetValue("namespace", out var nsObj))
                namespaceFilter = nsObj.ToString();

            string? namePattern = null;
            if (arguments.TryGetValue("name_pattern", out var npObj))
                namePattern = npObj?.ToString();

            string? cursor = null;
            if (arguments.TryGetValue("cursor", out var cursorObj))
                cursor = cursorObj.ToString();

            var (offset, pageSize) = DecodeCursor(cursor);

            System.Text.RegularExpressions.Regex? nameRegex = null;
            if (!string.IsNullOrEmpty(namePattern))
                nameRegex = BuildPatternRegex(namePattern!);

            var types = assembly.Modules
                .SelectMany(m => GetAllTypesRecursive(m.Types))
                .Where(t => {
                    if (!string.IsNullOrEmpty(namespaceFilter) &&
                        !t.Namespace.String.Equals(namespaceFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (nameRegex != null)
                        return nameRegex.IsMatch(t.Name.String) || nameRegex.IsMatch(t.FullName);
                    return true;
                })
                .Select(t => new
                {
                    FullName = t.FullName,
                    Namespace = t.Namespace.String,
                    Name = t.Name.String,
                    IsPublic = t.IsPublic,
                    IsClass = t.IsClass,
                    IsInterface = t.IsInterface,
                    IsEnum = t.IsEnum,
                    IsValueType = t.IsValueType,
                    IsAbstract = t.IsAbstract,
                    IsSealed = t.IsSealed,
                    BaseType = t.BaseType?.FullName ?? "None"
                })
                .ToList();

            return CreatePaginatedResponse(types, offset, pageSize);
        }

        public CallToolResult ResolveToken(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");
            if (!arguments.TryGetValue("token", out var tokenObj))
                throw new ArgumentException("token is required");

            string? filePath = arguments.TryGetValue("file_path", out var fpObj) ? fpObj?.ToString() : null;
            string? assemblyName = arguments.TryGetValue("assembly_name", out var asmObj) ? asmObj?.ToString() : null;

            AssemblyDef? assembly;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                assembly = FindAssemblyByFilePath(filePath!);
                if (assembly == null)
                    throw new ArgumentException($"No assembly loaded from path: {filePath}. Use dnspy_list_assemblies to see loaded FilePath values.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                    throw new ArgumentException("assembly_name is required unless file_path is provided");
                assembly = FindAssemblyByName(assemblyName!);
                if (assembly == null)
                    throw new ArgumentException($"Assembly not found: {assemblyName}");
            }

            var rawToken = ParseMetadataToken(tokenObj);
            var mdToken = new MDToken(rawToken);

            object? resolved = null;
            ModuleDef? resolvedModule = null;

            foreach (var module in assembly.Modules)
            {
                try
                {
                    resolved = module.ResolveToken(rawToken);
                    if (resolved != null)
                    {
                        resolvedModule = module;
                        break;
                    }
                }
                catch
                {
                    // Ignore and continue checking other modules in the assembly.
                }
            }

            if (resolved == null || resolvedModule == null)
                throw new ArgumentException($"Metadata token 0x{rawToken:X8} could not be resolved in assembly '{assembly.Name.String}'.");

            var payload = DescribeResolvedToken(assembly, resolvedModule, mdToken, resolved);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> {
                    new ToolContent { Text = json }
                }
            };
        }

        static IEnumerable<TypeDef> GetAllTypesRecursive(IEnumerable<TypeDef> types) {
            foreach (var type in types) {
                yield return type;
                foreach (var nested in GetAllTypesRecursive(type.NestedTypes))
                    yield return nested;
            }
        }

        public CallToolResult ListNativeModules(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("assembly_name", out var assemblyNameObj))
                throw new ArgumentException("assembly_name is required");

            var assemblyName = assemblyNameObj.ToString() ?? string.Empty;
            var assembly = FindAssemblyByName(assemblyName);
            if (assembly == null)
                throw new ArgumentException($"Assembly not found: {assemblyName}");

            var modules = new Dictionary<string, HashSet<object>>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in assembly.Modules.SelectMany(m => m.Types))
            {
                foreach (var method in type.Methods)
                {
                    try
                    {
                        foreach (var ca in method.CustomAttributes)
                        {
                            var at = ca.AttributeType.FullName;
                            if (!string.IsNullOrEmpty(at) && at.EndsWith("DllImportAttribute"))
                            {
                                var dllName = ca.ConstructorArguments.Count > 0 ? ca.ConstructorArguments[0].Value?.ToString() ?? string.Empty : string.Empty;
                                if (string.IsNullOrEmpty(dllName))
                                    continue;

                                if (!modules.TryGetValue(dllName, out var set))
                                {
                                    set = new HashSet<object>();
                                    modules[dllName] = set;
                                }

                                set.Add(new { Type = type.FullName, Method = method.Name.String });
                            }
                        }
                    }
                    catch { /* tolerate metadata issues */ }
                }
            }

            var list = modules.Select(kvp => new
            {
                ModuleName = kvp.Key,
                PathHint = string.Empty,
                ImportedBy = kvp.Value.ToList()
            }).ToList();

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult { Content = new List<ToolContent> { new ToolContent { Text = json } } };
        }

        public CallToolResult GetNativeImports(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var assembly = ResolveAssembly(arguments);
            var cursor = arguments.TryGetValue("cursor", out var cursorObj) ? cursorObj?.ToString() : null;
            var (offset, pageSize) = DecodeCursor(cursor);

            var items = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .SelectMany(a => a.Methods)
                .Where(a => a.HasImplMap && a.ImplMap != null)
                .Select(a => {
                    var payload = DescribeReferenceWithMemberIdStatic(a);
                    payload["dll_name"] = a.ImplMap?.Module?.Name?.String ?? string.Empty;
                    payload["native_name"] = a.ImplMap?.Name?.String ?? a.Name.String;
                    payload["attributes"] = a.ImplMap?.Attributes.ToString();
                    payload["is_pinvoke"] = true;
                    return payload;
                })
                .OrderBy(a => a["dll_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a["full_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return CreatePaginatedResponse(items, offset, pageSize);
        }

        public CallToolResult GetNativeExports(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            var filePath = ResolvePortableExecutablePath(arguments);
            var payload = ExtractNativeExportsFromPe(filePath);
            return ToolResponseFactory.Json(payload);
        }

        /// <summary>
        /// Scans the raw PE file bytes for printable ASCII and UTF-16 strings.
        /// Useful for finding URLs, keys, and other plaintext data in obfuscated assemblies.
        /// </summary>
        public CallToolResult ScanPeStrings(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("assembly_name or file_path is required");

            string? filePath = null;

            // Prefer direct file_path if provided — avoids the ambiguity where two loaded
            // assemblies share the same internal name (e.g. original packed + unpacked copy).
            if (arguments.TryGetValue("file_path", out var fpObj) && fpObj?.ToString() is string fpStr && !string.IsNullOrEmpty(fpStr))
            {
                if (!System.IO.File.Exists(fpStr))
                    throw new ArgumentException($"File not found: {fpStr}");
                filePath = fpStr;
            }
            else
            {
                // Fall back to assembly name lookup
                if (!arguments.TryGetValue("assembly_name", out var asmObj) || asmObj == null)
                    throw new ArgumentException("assembly_name is required (or provide file_path for direct path access)");

                var assemblyName = asmObj.ToString() ?? string.Empty;

                // When file_path is omitted, prefer the module whose Filename matches the
                // assembly_name hint — this avoids picking the wrong entry when two assemblies
                // share the same internal name (e.g. packed original + unpacked copy).
                var moduleNode = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    documentTreeView.GetAllModuleNodes()
                        .Where(m => m.Document?.AssemblyDef != null &&
                            m.Document.AssemblyDef.Name.String.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(m => {
                            // Prefer the node whose file name contains the assembly_name hint
                            var fn = System.IO.Path.GetFileNameWithoutExtension(m.Document?.Filename ?? "");
                            return fn.IndexOf(assemblyName, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0;
                        })
                        .FirstOrDefault());

                if (moduleNode == null)
                    throw new ArgumentException($"Assembly not found: {assemblyName}. If you have multiple files with the same internal name, use file_path instead.");

                filePath = moduleNode.Document?.Filename;
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                    throw new ArgumentException(
                        $"File not found on disk: {filePath ?? "(null)"}. " +
                        "This can happen with assemblies loaded from memory (PID). " +
                        "Use file_path to specify the file directly.");
            }

            int minLength = 5;
            if (arguments.TryGetValue("min_length", out var minLenObj) &&
                int.TryParse(minLenObj.ToString(), out var ml) && ml > 0)
                minLength = ml;

            bool includeAscii = true;
            bool includeUtf16 = true;
            if (arguments.TryGetValue("encoding", out var encodingObj) && !string.IsNullOrWhiteSpace(encodingObj?.ToString()))
            {
                switch (encodingObj!.ToString()!.Trim().ToLowerInvariant())
                {
                    case "ascii":
                        includeAscii = true;
                        includeUtf16 = false;
                        break;
                    case "unicode":
                    case "utf16":
                    case "utf-16":
                        includeAscii = false;
                        includeUtf16 = true;
                        break;
                    case "both":
                        includeAscii = true;
                        includeUtf16 = true;
                        break;
                }
            }
            if (arguments.TryGetValue("include_utf16", out var utf16Obj))
                bool.TryParse(utf16Obj.ToString(), out includeUtf16);

            string? filterPattern = null;
            if (arguments.TryGetValue("filter_pattern", out var fObj))
                filterPattern = fObj.ToString();

            System.Text.RegularExpressions.Regex? filterRx = null;
            if (!string.IsNullOrEmpty(filterPattern))
                filterRx = new System.Text.RegularExpressions.Regex(filterPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            var bytes = System.IO.File.ReadAllBytes(filePath);
            var found = new List<(string Encoding, string Offset, string Value)>();
            var seen = new HashSet<string>();

            int start = -1;
            if (includeAscii)
            {
                // Scan ASCII strings
                for (int i = 0; i <= bytes.Length; i++)
                {
                    bool printable = i < bytes.Length && bytes[i] >= 0x20 && bytes[i] < 0x7F;
                    if (printable)
                    {
                        if (start < 0) start = i;
                    }
                    else
                    {
                        if (start >= 0)
                        {
                            int len = i - start;
                            if (len >= minLength)
                            {
                                var s = Encoding.ASCII.GetString(bytes, start, len);
                                if ((filterRx == null || filterRx.IsMatch(s)) && seen.Add(s))
                                    found.Add(("ASCII", $"0x{start:X}", s));
                            }
                            start = -1;
                        }
                    }
                }
            }

            // Scan UTF-16 LE strings
            if (includeUtf16)
            {
                start = -1;
                for (int i = 0; i <= bytes.Length - 1; i += 2)
                {
                    bool printable = i + 1 < bytes.Length && bytes[i] >= 0x20 && bytes[i] < 0x7F && bytes[i + 1] == 0x00;
                    if (printable)
                    {
                        if (start < 0) start = i;
                    }
                    else
                    {
                        if (start >= 0)
                        {
                            int len = i - start;
                            if (len / 2 >= minLength)
                            {
                                var s = Encoding.Unicode.GetString(bytes, start, len);
                                if ((filterRx == null || filterRx.IsMatch(s)) && seen.Add(s))
                                    found.Add(("UTF-16", $"0x{start:X}", s));
                            }
                            start = -1;
                        }
                    }
                }
            }

            // Highlight suspicious strings (URLs, IPs, emails, paths)
            var suspicious = new System.Text.RegularExpressions.Regex(
                @"https?://|ftp://|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|@[a-z0-9-]+\.[a-z]{2,}|[A-Z]:\\|/[a-z]+/|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var allStrings = found.Select(t => new { Encoding = t.Encoding, Offset = t.Offset, Value = t.Value }).ToList();
            var suspiciousStrings = found
                .Where(t => suspicious.IsMatch(t.Value))
                .Select(t => new { Encoding = t.Encoding, Offset = t.Offset, Value = t.Value })
                .ToList();

            var result = JsonSerializer.Serialize(new
            {
                FilePath = filePath,
                FileSize = bytes.Length,
                TotalStrings = found.Count,
                SuspiciousStrings = suspiciousStrings,
                AllStrings = allStrings
            }, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = result } }
            };
        }

        // ── load_assembly ────────────────────────────────────────────────────────

        /// <summary>
        /// Load an assembly into dnSpy from a file on disk or from a running process by PID.
        /// Arguments:
        ///   file_path  – absolute path to a .NET assembly or memory dump on disk.
        ///   memory_layout – (bool, default false) when true the file is treated as a
        ///                   raw memory-layout dump rather than a normal PE file.
        ///   pid        – PID of the running process (alternative to file_path).
        ///   module_name – name/filename filter for the module to dump (optional with pid).
        ///   process_id – alias for pid.
        /// </summary>
        public CallToolResult LoadAssembly(Dictionary<string, object>? arguments) {
            if (arguments == null)
                throw new ArgumentException("Arguments required: provide 'file_path' or 'pid'");

            // ── Mode 1: load from file ────────────────────────────────────────────
            if (arguments.TryGetValue("file_path", out var fpObj) && fpObj?.ToString() is string filePath && !string.IsNullOrWhiteSpace(filePath)) {
                if (!File.Exists(filePath))
                    throw new ArgumentException($"File not found: {filePath}");

                bool memoryLayout = false;
                if (arguments.TryGetValue("memory_layout", out var mlObj))
                    bool.TryParse(mlObj?.ToString(), out memoryLayout);

                IDsDocument doc;
                string displayName;

                if (memoryLayout) {
                    // Load raw bytes with memory-layout flag so dnlib can parse
                    // sections from VAs instead of file offsets.
                    var bytes = File.ReadAllBytes(filePath);
                    var filename = Path.GetFileName(filePath);
                    var docInfo = DsDocumentInfo.CreateInMemory(() => (bytes, isFileLayout: false), filename);
                    doc = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        documentService.TryGetOrCreate(docInfo, isAutoLoaded: false))
                        ?? throw new InvalidOperationException("Failed to create in-memory document");
                    displayName = doc.AssemblyDef?.Name.String
                        ?? doc.ModuleDef?.Name.String
                        ?? filename;
                }
                else {
                    var docInfo = DsDocumentInfo.CreateDocument(filePath);
                    doc = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        documentService.TryGetOrCreate(docInfo, isAutoLoaded: false))
                        ?? throw new InvalidOperationException("Failed to load document");
                    displayName = doc.AssemblyDef?.Name.String
                        ?? doc.ModuleDef?.Name.String
                        ?? Path.GetFileNameWithoutExtension(filePath);
                }

                var result = JsonSerializer.Serialize(new {
                    Status = "loaded",
                    AssemblyName = displayName,
                    FilePath = filePath,
                    MemoryLayout = memoryLayout,
                    IsAssembly = doc.AssemblyDef != null,
                    Version = doc.AssemblyDef?.Version?.ToString() ?? "N/A"
                }, new JsonSerializerOptions { WriteIndented = true });

                return new CallToolResult {
                    Content = new List<ToolContent> { new ToolContent { Text = result } }
                };
            }

            // ── Mode 2: dump from running process ────────────────────────────────
            int pid = 0;
            if (arguments.TryGetValue("pid", out var pidObj) && pidObj?.ToString() is string pidStr)
                int.TryParse(pidStr, out pid);
            if (pid == 0 && arguments.TryGetValue("process_id", out var pidObj2) && pidObj2?.ToString() is string pidStr2)
                int.TryParse(pidStr2, out pid);

            if (pid == 0)
                throw new ArgumentException("Provide either 'file_path' (load from disk) or 'pid' (dump from running process)");

            string? moduleFilter = null;
            if (arguments.TryGetValue("module_name", out var mnObj))
                moduleFilter = mnObj?.ToString();

            var mgr = dbgManager.Value;
            if (!mgr.IsDebugging)
                throw new InvalidOperationException("No active debug session. Start or attach to a process first, then use this tool.");

            // Find process
            var process = pid > 0
                ? mgr.Processes.FirstOrDefault(p => p.Id == pid)
                : mgr.Processes.FirstOrDefault();
            if (process == null)
                throw new ArgumentException($"Process {pid} not found in active debug session");

            // Find the target .NET module
            DbgModule? target = null;
            foreach (var rt in process.Runtimes) {
                foreach (var mod in rt.Modules) {
                    var name = mod.Name ?? mod.Filename ?? "";
                    if (string.IsNullOrEmpty(moduleFilter) && mod.IsExe) {
                        target = mod; break;
                    }
                    if (!string.IsNullOrEmpty(moduleFilter) &&
                        (Path.GetFileName(name).Equals(moduleFilter, StringComparison.OrdinalIgnoreCase) ||
                         name.IndexOf(moduleFilter, StringComparison.OrdinalIgnoreCase) >= 0)) {
                        target = mod; break;
                    }
                }
                if (target != null) break;
            }
            if (target == null)
                throw new ArgumentException(
                    string.IsNullOrEmpty(moduleFilter)
                        ? "No EXE module found. Pass 'module_name' to specify one."
                        : $"Module '{moduleFilter}' not found in process {process.Id}");

            // Dump module bytes using IDbgDotNetRuntime.GetRawModuleBytes
            byte[]? moduleBytes = null;
            bool isFileLayout = false;

            // Find runtime that owns this module
            DbgRuntime? owningRuntime = null;
            foreach (var rt in process.Runtimes) {
                if (rt.Modules.Contains(target)) { owningRuntime = rt; break; }
            }

            if (owningRuntime?.InternalRuntime is IDbgDotNetRuntime dnRuntime) {
                var rawResult = dnRuntime.GetRawModuleBytes(target);
                if (rawResult.RawBytes != null && rawResult.RawBytes.Length > 0) {
                    moduleBytes = rawResult.RawBytes;
                    isFileLayout = rawResult.IsFileLayout;
                }
            }

            // Fallback: read via process.ReadMemory
            if (moduleBytes == null) {
                if (!target.HasAddress)
                    throw new InvalidOperationException($"Module '{target.Name}' has no address/size; cannot dump");
                moduleBytes = process.ReadMemory(target.Address, (int)target.Size);
                isFileLayout = false;
            }

            if (moduleBytes == null || moduleBytes.Length == 0)
                throw new InvalidOperationException($"Failed to read module bytes for '{target.Name}'");

            var modFilename = Path.GetFileName(target.Filename ?? target.Name ?? "module.dll");
            var inMemDocInfo = DsDocumentInfo.CreateInMemory(() => (moduleBytes, isFileLayout), modFilename);
            var loadedDoc = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentService.TryGetOrCreate(inMemDocInfo, isAutoLoaded: false))
                ?? throw new InvalidOperationException("Failed to create in-memory document from process dump");

            var asmName = loadedDoc.AssemblyDef?.Name.String
                ?? loadedDoc.ModuleDef?.Name.String
                ?? modFilename;

            var resultJson = JsonSerializer.Serialize(new {
                Status = "loaded",
                AssemblyName = asmName,
                SourcePid = process.Id,
                ModuleAddress = $"0x{target.Address:X}",
                ModuleSize = moduleBytes.Length,
                IsFileLayout = isFileLayout,
                IsAssembly = loadedDoc.AssemblyDef != null,
                Version = loadedDoc.AssemblyDef?.Version?.ToString() ?? "N/A"
            }, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = resultJson } }
            };
        }

        AssemblyDef? FindAssemblyByName(string name)
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(m => m.Document?.AssemblyDef)
                    .FirstOrDefault(a => a != null && a.Name.String.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        static uint ParseMetadataToken(object tokenObj)
        {
            switch (tokenObj)
            {
                case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetUInt32(out var numericToken):
                    return numericToken;
                case JsonElement je when je.ValueKind == JsonValueKind.String:
                    return ParseMetadataTokenString(je.GetString());
                case uint value:
                    return value;
                case int value when value >= 0:
                    return (uint)value;
                case long value when value >= 0 && value <= uint.MaxValue:
                    return (uint)value;
                default:
                    return ParseMetadataTokenString(tokenObj?.ToString());
            }
        }

        static uint ParseMetadataTokenString(string? tokenText)
        {
            if (string.IsNullOrWhiteSpace(tokenText))
                throw new ArgumentException("token must be a non-empty metadata token string or integer");

            var trimmed = tokenText.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
                if (uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var prefixedHexToken))
                    return prefixedHexToken;
                throw new ArgumentException($"Invalid metadata token: {tokenText}");
            }

            if (uint.TryParse(trimmed, out var decimalToken))
                return decimalToken;

            if (uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var hexToken))
                return hexToken;

            throw new ArgumentException($"Invalid metadata token: {tokenText}");
        }

        static Dictionary<string, object?> DescribeResolvedToken(AssemblyDef assembly, ModuleDef module, MDToken token, object resolved)
        {
            var payload = new Dictionary<string, object?>
            {
                ["assembly_name"] = assembly.Name.String,
                ["module_name"] = module.Name.String,
                ["module_mvid"] = (module.Mvid ?? Guid.Empty).ToString("N"),
                ["token"] = $"0x{token.Raw:X8}",
                ["token_table"] = token.Table.ToString(),
                ["token_rid"] = token.Rid,
                ["resolved_kind"] = resolved.GetType().Name
            };

            switch (resolved)
            {
                case TypeDef typeDef:
                    payload["name"] = typeDef.Name.String;
                    payload["full_name"] = typeDef.FullName;
                    payload["namespace"] = typeDef.Namespace.String;
                    payload["declaring_type"] = typeDef.DeclaringType?.FullName;
                    break;

                case MethodDef methodDef:
                    payload["name"] = methodDef.Name.String;
                    payload["full_name"] = methodDef.FullName;
                    payload["declaring_type"] = methodDef.DeclaringType?.FullName;
                    payload["signature"] = methodDef.MethodSig?.ToString();
                    break;

                case FieldDef fieldDef:
                    payload["name"] = fieldDef.Name.String;
                    payload["full_name"] = fieldDef.FullName;
                    payload["declaring_type"] = fieldDef.DeclaringType?.FullName;
                    payload["field_type"] = fieldDef.FieldType?.FullName;
                    break;

                case PropertyDef propertyDef:
                    payload["name"] = propertyDef.Name.String;
                    payload["full_name"] = propertyDef.FullName;
                    payload["declaring_type"] = propertyDef.DeclaringType?.FullName;
                    payload["property_type"] = propertyDef.PropertySig?.RetType?.FullName;
                    break;

                case EventDef eventDef:
                    payload["name"] = eventDef.Name.String;
                    payload["full_name"] = eventDef.FullName;
                    payload["declaring_type"] = eventDef.DeclaringType?.FullName;
                    payload["event_type"] = eventDef.EventType?.FullName;
                    break;

                case TypeRef typeRef:
                    payload["name"] = typeRef.Name.String;
                    payload["full_name"] = typeRef.FullName;
                    payload["namespace"] = typeRef.Namespace.String;
                    payload["resolution_scope"] = typeRef.ResolutionScope?.ToString();
                    break;

                case MemberRef memberRef:
                    payload["name"] = memberRef.Name.String;
                    payload["full_name"] = memberRef.FullName;
                    payload["declaring_type"] = memberRef.DeclaringType?.FullName;
                    break;

                case AssemblyRef assemblyRef:
                    payload["name"] = assemblyRef.Name.String;
                    payload["full_name"] = assemblyRef.FullName;
                    payload["version"] = assemblyRef.Version?.ToString();
                    break;

                case ModuleRef moduleRef:
                    payload["name"] = moduleRef.Name.String;
                    break;

                case ManifestResource resource:
                    payload["name"] = resource.Name.String;
                    payload["visibility"] = resource.Visibility.ToString();
                    payload["offset"] = resource.Offset;
                    break;

                case GenericParam genericParam:
                    payload["name"] = genericParam.Name.String;
                    payload["owner"] = genericParam.Owner?.ToString();
                    payload["number"] = genericParam.Number;
                    break;

                case Parameter parameter:
                    payload["name"] = parameter.Name;
                    payload["declaring_method"] = parameter.Method?.FullName;
                    payload["parameter_type"] = parameter.Type?.FullName;
                    payload["index"] = parameter.Index;
                    break;

                default:
                    payload["display_name"] = resolved.ToString();
                    break;
            }

            return payload;
        }

        AssemblyDef ResolveAssembly(Dictionary<string, object> arguments)
        {
            string? filePath = arguments.TryGetValue("file_path", out var fpObj) ? fpObj?.ToString() : null;
            string? assemblyName = arguments.TryGetValue("assembly_name", out var asmObj) ? asmObj?.ToString() : null;

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var byPath = FindAssemblyByFilePath(filePath!);
                if (byPath == null)
                    throw new ArgumentException($"No assembly loaded from path: {filePath}. Use dnspy_list_assemblies to see loaded FilePath values.");
                return byPath;
            }

            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new ArgumentException("assembly_name is required unless file_path is provided");

            var byName = FindAssemblyByName(assemblyName!);
            if (byName == null)
                throw new ArgumentException($"Assembly not found: {assemblyName}");
            return byName;
        }

        ModuleDefMD ResolveMetadataModule(Dictionary<string, object> arguments)
        {
            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.OfType<ModuleDefMD>().FirstOrDefault()
                ?? throw new ArgumentException($"Assembly '{assembly.Name.String}' has no metadata-backed module.");

            if (module is not ModuleDefMD moduleDefMd || moduleDefMd.Metadata == null)
                throw new ArgumentException($"Assembly '{assembly.Name.String}' does not expose dnlib metadata.");

            return moduleDefMd;
        }

        object ResolveReference(Dictionary<string, object> arguments)
        {
            var assembly = ResolveAssembly(arguments);

            if (arguments.TryGetValue("token", out var tokenObj) || arguments.TryGetValue("metadata_token", out tokenObj))
            {
                var rawToken = ParseMetadataToken(tokenObj!);
                foreach (var module in assembly.Modules)
                {
                    try
                    {
                        var resolved = module.ResolveToken(rawToken);
                        if (resolved != null)
                            return resolved;
                    }
                    catch
                    {
                    }
                }

                throw new ArgumentException($"Metadata token 0x{rawToken:X8} could not be resolved in assembly '{assembly.Name.String}'.");
            }

            var typeFullName = arguments.TryGetValue("type_full_name", out var typeObj) ? typeObj?.ToString() : null;
            if (string.IsNullOrWhiteSpace(typeFullName))
                return assembly.ManifestModule != null ? (object)assembly.ManifestModule : assembly;

            var type = FindTypeInAssembly(assembly, typeFullName!);
            if (type == null)
                throw new ArgumentException($"Type not found: {typeFullName}");

            if (arguments.TryGetValue("method_name", out var methodObj) && !string.IsNullOrWhiteSpace(methodObj?.ToString()))
            {
                var methodName = methodObj!.ToString()!;
                var methodSignature = arguments.TryGetValue("method_signature", out var sigObj) ? sigObj?.ToString() : null;
                var method = type.Methods.FirstOrDefault(a =>
                    a.Name.String.Equals(methodName, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(methodSignature) || string.Equals(a.MethodSig?.ToString(), methodSignature, StringComparison.Ordinal)));
                if (method == null)
                    throw new ArgumentException($"Method not found: {methodName}");
                return method;
            }

            if (arguments.TryGetValue("field_name", out var fieldObj) && !string.IsNullOrWhiteSpace(fieldObj?.ToString()))
            {
                var fieldName = fieldObj!.ToString()!;
                var field = type.Fields.FirstOrDefault(a => a.Name.String.Equals(fieldName, StringComparison.Ordinal));
                if (field == null)
                    throw new ArgumentException($"Field not found: {fieldName}");
                return field;
            }

            if (arguments.TryGetValue("property_name", out var propertyObj) && !string.IsNullOrWhiteSpace(propertyObj?.ToString()))
            {
                var propertyName = propertyObj!.ToString()!;
                var property = type.Properties.FirstOrDefault(a => a.Name.String.Equals(propertyName, StringComparison.Ordinal));
                if (property == null)
                    throw new ArgumentException($"Property not found: {propertyName}");
                return property;
            }

            if (arguments.TryGetValue("event_name", out var eventObj) && !string.IsNullOrWhiteSpace(eventObj?.ToString()))
            {
                var eventName = eventObj!.ToString()!;
                var @event = type.Events.FirstOrDefault(a => a.Name.String.Equals(eventName, StringComparison.Ordinal));
                if (@event == null)
                    throw new ArgumentException($"Event not found: {eventName}");
                return @event;
            }

            return type;
        }

        object ResolveMemberIdCore(string memberId)
        {
            var parts = memberId.Split(':');
            if (parts.Length != 3)
                throw new ArgumentException($"Invalid member_id format: {memberId}");

            if (!Guid.TryParseExact(parts[0], "N", out var mvid))
                throw new ArgumentException($"Invalid module MVID in member_id: {memberId}");

            if (!uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var rawToken))
                throw new ArgumentException($"Invalid metadata token in member_id: {memberId}");

            var kindPart = parts[2];
            if (kindPart.Length != 1 || "TMFPE".IndexOf(kindPart[0]) < 0)
                throw new ArgumentException($"Invalid member kind in member_id: {memberId}");

            var module = FindLoadedModuleByMvid(mvid)
                ?? throw new ArgumentException($"No loaded module found for MVID {mvid:N}. Load the assembly first.");

            var resolved = module.ResolveToken(rawToken)
                ?? throw new ArgumentException($"Metadata token 0x{rawToken:X8} could not be resolved in module {module.Name}.");

            return resolved;
        }

        ModuleDef? FindLoadedModuleByMvid(Guid mvid)
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(m => m.Document?.ModuleDef)
                    .FirstOrDefault(m => m != null && (m.Mvid ?? Guid.Empty) == mvid));
        }

        static string? TryGetTargetFramework(AssemblyDef assembly)
        {
            try
            {
                var targetFramework = assembly.CustomAttributes.Find("System.Runtime.Versioning.TargetFrameworkAttribute");
                if (targetFramework == null || targetFramework.ConstructorArguments.Count == 0)
                    return null;
                return targetFramework.ConstructorArguments[0].Value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        static Dictionary<string, object?> BuildTableValidationResult(string tableName, uint rowCount, Func<uint, bool> canReadRow)
        {
            int failedRows = 0;
            for (uint rid = 1; rid <= rowCount; rid++)
            {
                if (!canReadRow(rid))
                    failedRows++;
            }

            return new Dictionary<string, object?> {
                ["table_name"] = tableName,
                ["row_count"] = rowCount,
                ["failed_rows"] = failedRows,
                ["passed"] = failedRows == 0
            };
        }

        static bool ReadBoolean(Dictionary<string, object> arguments, string key, bool defaultValue)
        {
            if (!arguments.TryGetValue(key, out var valueObj) || valueObj == null)
                return defaultValue;

            if (valueObj is bool boolValue)
                return boolValue;

            if (valueObj is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.True)
                    return true;
                if (element.ValueKind == JsonValueKind.False)
                    return false;
                if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed))
                    return parsed;
            }

            return bool.TryParse(valueObj.ToString(), out var result) ? result : defaultValue;
        }

        static string NormalizeHeapName(string? heapName)
        {
            var normalized = (heapName ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "strings" or "#strings" => "strings",
                "blob" or "#blob" => "blob",
                "guid" or "#guid" => "guid",
                "userstrings" or "us" or "#us" => "userstrings",
                _ => throw new ArgumentException("heap must be one of: strings, blob, guid, userstrings")
            };
        }

        IEnumerable<Dictionary<string, object?>> EnumerateMetadataHeapEntries(ModuleDefMD module, string heapName)
        {
            switch (heapName)
            {
                case "strings":
                    return EnumerateStringsHeapEntries(module).ToList();
                case "guid":
                    return EnumerateGuidHeapEntries(module).ToList();
                case "userstrings":
                    return EnumerateUserStringsHeapEntries(module).ToList();
                case "blob":
                    return EnumerateBlobHeapEntries(module).ToList();
                default:
                    throw new ArgumentException($"Unsupported heap: {heapName}");
            }
        }

        Dictionary<string, object?> DescribeMetadataHeapEntry(ModuleDefMD module, string heapName, uint heapOffset)
        {
            switch (heapName)
            {
                case "strings":
                    return new Dictionary<string, object?> {
                        ["offset"] = $"0x{heapOffset:X8}",
                        ["value"] = module.Metadata.StringsStream.ReadNoNull(heapOffset)
                    };
                case "guid":
                    var guidIndex = heapOffset == 0 ? 0 : (int)heapOffset;
                    var guidValue = guidIndex <= 0 ? null : module.Metadata.GuidStream.Read((uint)guidIndex);
                    return new Dictionary<string, object?> {
                        ["index"] = guidIndex,
                        ["offset"] = $"0x{heapOffset:X8}",
                        ["value"] = guidValue?.ToString("D")
                    };
                case "userstrings":
                    return new Dictionary<string, object?> {
                        ["offset"] = $"0x{heapOffset:X8}",
                        ["value"] = module.Metadata.USStream.Read(heapOffset)
                    };
                case "blob":
                    var blobBytes = module.Metadata.BlobStream.Read(heapOffset) ?? Array.Empty<byte>();
                    return new Dictionary<string, object?> {
                        ["offset"] = $"0x{heapOffset:X8}",
                        ["size"] = blobBytes.Length,
                        ["hex_preview"] = ToHexPreview(blobBytes, 64),
                        ["base64"] = Convert.ToBase64String(blobBytes)
                    };
                default:
                    throw new ArgumentException($"Unsupported heap: {heapName}");
            }
        }

        IEnumerable<Dictionary<string, object?>> EnumerateStringsHeapEntries(ModuleDefMD module)
        {
            var heapBytes = ReadHeapBytes(module, (uint)module.Metadata.StringsStream.StartOffset, (uint)module.Metadata.StringsStream.EndOffset);
            if (heapBytes.Length <= 1)
                yield break;

            int current = 1;
            while (current < heapBytes.Length)
            {
                var start = current;
                while (current < heapBytes.Length && heapBytes[current] != 0)
                    current++;

                if (current > start)
                {
                    yield return new Dictionary<string, object?> {
                        ["offset"] = $"0x{start:X8}",
                        ["value"] = Encoding.UTF8.GetString(heapBytes, start, current - start)
                    };
                }

                current++;
            }
        }

        IEnumerable<Dictionary<string, object?>> EnumerateGuidHeapEntries(ModuleDefMD module)
        {
            var heapBytes = ReadHeapBytes(module, (uint)module.Metadata.GuidStream.StartOffset, (uint)module.Metadata.GuidStream.EndOffset);
            if (heapBytes.Length == 0)
                yield break;

            var guidCount = heapBytes.Length / 16;
            for (int index = 1; index <= guidCount; index++)
            {
                var guid = module.Metadata.GuidStream.Read((uint)index);
                if (guid == null)
                    continue;

                yield return new Dictionary<string, object?> {
                    ["index"] = index,
                    ["offset"] = $"0x{((index - 1) * 16):X8}",
                    ["value"] = guid.Value.ToString("D")
                };
            }
        }

        IEnumerable<Dictionary<string, object?>> EnumerateUserStringsHeapEntries(ModuleDefMD module)
        {
            var heapBytes = ReadHeapBytes(module, (uint)module.Metadata.USStream.StartOffset, (uint)module.Metadata.USStream.EndOffset);
            if (heapBytes.Length <= 1)
                yield break;

            int current = 1;
            while (current < heapBytes.Length)
            {
                var entryOffset = current;
                if (!TryReadCompressedUInt(heapBytes, current, out var dataLength, out var prefixLength))
                    yield break;

                current += prefixLength;
                if (dataLength == 0 || current + dataLength > heapBytes.Length)
                    yield break;

                var entryBytes = new byte[dataLength];
                Buffer.BlockCopy(heapBytes, current, entryBytes, 0, dataLength);

                yield return new Dictionary<string, object?> {
                    ["offset"] = $"0x{entryOffset:X8}",
                    ["value"] = DecodeUserString(entryBytes),
                    ["raw_size"] = dataLength
                };

                current += dataLength;
            }
        }

        IEnumerable<Dictionary<string, object?>> EnumerateBlobHeapEntries(ModuleDefMD module)
        {
            var heapBytes = ReadHeapBytes(module, (uint)module.Metadata.BlobStream.StartOffset, (uint)module.Metadata.BlobStream.EndOffset);
            if (heapBytes.Length <= 1)
                yield break;

            int current = 1;
            while (current < heapBytes.Length)
            {
                var entryOffset = current;
                if (!TryReadCompressedUInt(heapBytes, current, out var dataLength, out var prefixLength))
                    yield break;

                current += prefixLength;
                if (dataLength == 0)
                {
                    yield return new Dictionary<string, object?> {
                        ["offset"] = $"0x{entryOffset:X8}",
                        ["size"] = 0,
                        ["hex_preview"] = string.Empty
                    };
                    continue;
                }

                if (current + dataLength > heapBytes.Length)
                    yield break;

                var entryBytes = new byte[dataLength];
                Buffer.BlockCopy(heapBytes, current, entryBytes, 0, dataLength);

                yield return new Dictionary<string, object?> {
                    ["offset"] = $"0x{entryOffset:X8}",
                    ["size"] = dataLength,
                    ["hex_preview"] = ToHexPreview(entryBytes, 64)
                };

                current += dataLength;
            }
        }

        static byte[] ReadHeapBytes(ModuleDefMD module, uint startOffset, uint endOffset)
        {
            if (endOffset <= startOffset)
                return Array.Empty<byte>();

            var allBytes = module.Metadata.PEImage.CreateReader().ToArray();
            var start = (int)startOffset;
            var size = (int)(endOffset - startOffset);

            if (start < 0 || start >= allBytes.Length || size <= 0)
                return Array.Empty<byte>();

            if (start + size > allBytes.Length)
                size = allBytes.Length - start;

            var result = new byte[size];
            Buffer.BlockCopy(allBytes, start, result, 0, size);
            return result;
        }

        static bool TryReadCompressedUInt(byte[] data, int offset, out int value, out int bytesRead)
        {
            value = 0;
            bytesRead = 0;

            if (offset < 0 || offset >= data.Length)
                return false;

            var first = data[offset];
            if ((first & 0x80) == 0)
            {
                value = first;
                bytesRead = 1;
                return true;
            }

            if ((first & 0xC0) == 0x80)
            {
                if (offset + 1 >= data.Length)
                    return false;

                value = ((first & 0x3F) << 8) | data[offset + 1];
                bytesRead = 2;
                return true;
            }

            if ((first & 0xE0) == 0xC0)
            {
                if (offset + 3 >= data.Length)
                    return false;

                value = ((first & 0x1F) << 24) |
                        (data[offset + 1] << 16) |
                        (data[offset + 2] << 8) |
                        data[offset + 3];
                bytesRead = 4;
                return true;
            }

            return false;
        }

        static string DecodeUserString(byte[] entryBytes)
        {
            if (entryBytes.Length == 0)
                return string.Empty;

            var textLength = entryBytes.Length > 0 ? entryBytes.Length - 1 : 0;
            if (textLength <= 0)
                return string.Empty;

            return Encoding.Unicode.GetString(entryBytes, 0, textLength);
        }

        static uint ParseUnsignedInteger(string text)
        {
            var normalized = text.Trim();
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(normalized.Substring(2), 16);
            return Convert.ToUInt32(normalized, System.Globalization.CultureInfo.InvariantCulture);
        }

        static string ToHexPreview(byte[] bytes, int maxBytes)
        {
            if (bytes.Length == 0)
                return string.Empty;

            var length = Math.Min(bytes.Length, maxBytes);
            var hex = BitConverter.ToString(bytes, 0, length).Replace("-", " ");
            return bytes.Length > maxBytes ? $"{hex} ..." : hex;
        }

        Dictionary<string, object?> DescribeReferenceWithMemberId(object resolved)
        {
            switch (resolved)
            {
                case TypeDef typeDef:
                    return BuildMemberPayload(typeDef.Module, typeDef.MDToken.Raw, 'T', new Dictionary<string, object?> {
                        ["assembly_name"] = typeDef.Module.Assembly?.Name.String,
                        ["module_name"] = typeDef.Module.Name,
                        ["name"] = typeDef.Name.String,
                        ["full_name"] = typeDef.FullName,
                        ["namespace"] = typeDef.Namespace.String,
                        ["declaring_type"] = typeDef.DeclaringType?.FullName
                    });

                case MethodDef methodDef:
                    return BuildMemberPayload(methodDef.Module, methodDef.MDToken.Raw, 'M', new Dictionary<string, object?> {
                        ["assembly_name"] = methodDef.Module.Assembly?.Name.String,
                        ["module_name"] = methodDef.Module.Name,
                        ["name"] = methodDef.Name.String,
                        ["full_name"] = methodDef.FullName,
                        ["declaring_type"] = methodDef.DeclaringType?.FullName,
                        ["signature"] = methodDef.MethodSig?.ToString()
                    });

                case FieldDef fieldDef:
                    return BuildMemberPayload(fieldDef.Module, fieldDef.MDToken.Raw, 'F', new Dictionary<string, object?> {
                        ["assembly_name"] = fieldDef.Module.Assembly?.Name.String,
                        ["module_name"] = fieldDef.Module.Name,
                        ["name"] = fieldDef.Name.String,
                        ["full_name"] = fieldDef.FullName,
                        ["declaring_type"] = fieldDef.DeclaringType?.FullName,
                        ["field_type"] = fieldDef.FieldType?.FullName
                    });

                case PropertyDef propertyDef:
                    return BuildMemberPayload(propertyDef.Module, propertyDef.MDToken.Raw, 'P', new Dictionary<string, object?> {
                        ["assembly_name"] = propertyDef.Module.Assembly?.Name.String,
                        ["module_name"] = propertyDef.Module.Name,
                        ["name"] = propertyDef.Name.String,
                        ["full_name"] = propertyDef.FullName,
                        ["declaring_type"] = propertyDef.DeclaringType?.FullName,
                        ["property_type"] = propertyDef.PropertySig?.RetType?.FullName
                    });

                case EventDef eventDef:
                    return BuildMemberPayload(eventDef.Module, eventDef.MDToken.Raw, 'E', new Dictionary<string, object?> {
                        ["assembly_name"] = eventDef.Module.Assembly?.Name.String,
                        ["module_name"] = eventDef.Module.Name,
                        ["name"] = eventDef.Name.String,
                        ["full_name"] = eventDef.FullName,
                        ["declaring_type"] = eventDef.DeclaringType?.FullName,
                        ["event_type"] = eventDef.EventType?.FullName
                    });

                case ModuleDef moduleDef:
                    return new Dictionary<string, object?> {
                        ["reference_kind"] = "ModuleDef",
                        ["assembly_name"] = moduleDef.Assembly?.Name.String,
                        ["module_name"] = moduleDef.Name,
                        ["file_path"] = moduleDef.Location,
                        ["mvid"] = (moduleDef.Mvid ?? Guid.Empty).ToString("N")
                    };

                case AssemblyDef assemblyDef:
                    return new Dictionary<string, object?> {
                        ["reference_kind"] = "AssemblyDef",
                        ["assembly_name"] = assemblyDef.Name.String,
                        ["full_name"] = assemblyDef.FullName,
                        ["version"] = assemblyDef.Version?.ToString(),
                        ["manifest_module_mvid"] = (assemblyDef.ManifestModule?.Mvid ?? Guid.Empty).ToString("N")
                    };

                default:
                    return new Dictionary<string, object?> {
                        ["reference_kind"] = resolved.GetType().Name,
                        ["display_text"] = resolved.ToString()
                    };
            }
        }

        string DecompileResolvedReference(object resolved)
        {
            switch (resolved)
            {
                case TypeDef typeDef:
                    return DecompileType(typeDef);
                case MethodDef methodDef:
                    return DecompileMethod(methodDef);
                case FieldDef fieldDef when fieldDef.DeclaringType != null:
                    return DecompileType(fieldDef.DeclaringType);
                case PropertyDef propertyDef when propertyDef.DeclaringType != null:
                    return DecompileType(propertyDef.DeclaringType);
                case EventDef eventDef when eventDef.DeclaringType != null:
                    return DecompileType(eventDef.DeclaringType);
                default:
                    throw new ArgumentException($"Decompilation is not supported for resolved reference kind '{resolved.GetType().Name}'.");
            }
        }

        string DecompileType(TypeDef type)
        {
            var output = new StringBuilderDecompilerOutput();
            var ctx = new DecompilationContext { CancellationToken = System.Threading.CancellationToken.None };
            decompilerService.Decompiler.Decompile(type, output, ctx);
            return output.ToString();
        }

        string DecompileMethod(MethodDef method)
        {
            var output = new StringBuilderDecompilerOutput();
            var ctx = new DecompilationContext { CancellationToken = System.Threading.CancellationToken.None };
            decompilerService.Decompiler.Decompile(method, output, ctx);
            return output.ToString();
        }

        static string GetDecompileScope(object resolved) =>
            resolved switch
            {
                TypeDef => "type",
                MethodDef => "method",
                FieldDef => "declaring_type",
                PropertyDef => "declaring_type",
                EventDef => "declaring_type",
                _ => "unsupported"
            };

        static List<string> ReadStringArray(object value)
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.String)
                    .Select(a => a.GetString() ?? string.Empty)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

            if (value is IEnumerable<object> objectEnumerable)
                return objectEnumerable.Select(a => a?.ToString() ?? string.Empty)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();

            if (value is IEnumerable<string> stringEnumerable)
                return stringEnumerable.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

            return new List<string>();
        }

        static string BuildAstOutlineCacheKey(string? memberId, string source, bool includeInvocations)
        {
            var sourceHash = ComputeSha256Hex(source);
            return $"{memberId ?? "inline"}:{includeInvocations}:{sourceHash}";
        }

        static bool TryGetCachedAstOutline(string cacheKey, out Dictionary<string, object?> payload)
        {
            lock (astOutlineCacheLock)
            {
                if (astOutlineCache.TryGetValue(cacheKey, out payload!))
                    return true;
            }

            payload = null!;
            return false;
        }

        static void AddAstOutlineCacheEntry(string cacheKey, Dictionary<string, object?> payload)
        {
            lock (astOutlineCacheLock)
            {
                if (astOutlineCache.ContainsKey(cacheKey))
                    return;

                astOutlineCache[cacheKey] = payload;
                astOutlineCacheOrder.Enqueue(cacheKey);

                while (astOutlineCacheOrder.Count > MaxAstOutlineCacheEntries)
                {
                    var oldest = astOutlineCacheOrder.Dequeue();
                    astOutlineCache.Remove(oldest);
                }
            }
        }

        static string ComputeSha256Hex(string text)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        static Dictionary<string, object?> BuildAstOutlinePayload(string source, bool includeInvocations, string? memberId, string sourceOrigin, string? sourceName)
        {
            var parseResult = ParseSourceForOutline(source);
            var diagnostics = parseResult.Tree.GetDiagnostics()
                .Where(a => a.Severity == DiagnosticSeverity.Warning || a.Severity == DiagnosticSeverity.Error)
                .Select(a => new Dictionary<string, object?> {
                    ["id"] = a.Id,
                    ["severity"] = a.Severity.ToString(),
                    ["message"] = a.GetMessage(),
                    ["start"] = a.Location.SourceSpan.Start,
                    ["length"] = a.Location.SourceSpan.Length
                })
                .ToList();

            var outline = BuildOutlineMembers(parseResult.TopLevelMembers, includeInvocations);
            return new Dictionary<string, object?> {
                ["member_id"] = memberId,
                ["source_name"] = sourceName,
                ["source_origin"] = sourceOrigin,
                ["wrapped_source"] = parseResult.Wrapped,
                ["include_invocations"] = includeInvocations,
                ["error_count"] = diagnostics.Count(a => string.Equals(a["severity"]?.ToString(), "Error", StringComparison.Ordinal)),
                ["warning_count"] = diagnostics.Count(a => string.Equals(a["severity"]?.ToString(), "Warning", StringComparison.Ordinal)),
                ["diagnostics"] = diagnostics,
                ["outline"] = outline
            };
        }

        static (SyntaxTree Tree, IReadOnlyList<MemberDeclarationSyntax> TopLevelMembers, bool Wrapped) ParseSourceForOutline(string source)
        {
            var parseOptions = new CSharpParseOptions(kind: SourceCodeKind.Regular);
            var initialTree = CSharpSyntaxTree.ParseText(source, parseOptions);
            var initialRoot = (CompilationUnitSyntax)initialTree.GetRoot();
            var initialMembers = initialRoot.Members;

            if (initialMembers.Count > 0 || LooksLikeCompilationUnit(source))
                return (initialTree, initialMembers.ToList(), false);

            var wrappedSource = "namespace __DnspyMcpOutlineContainer {\ninternal sealed class __DnspyMcpMethodContainer {\n" + source + "\n}\n}";
            var wrappedTree = CSharpSyntaxTree.ParseText(wrappedSource, parseOptions);
            var wrappedRoot = (CompilationUnitSyntax)wrappedTree.GetRoot();
            var namespaceNode = wrappedRoot.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            var containerType = namespaceNode?.Members.OfType<ClassDeclarationSyntax>().FirstOrDefault();
            var members = containerType?.Members.ToList() ?? new List<MemberDeclarationSyntax>();
            return (wrappedTree, members, true);
        }

        static bool LooksLikeCompilationUnit(string source) =>
            source.IndexOf("namespace ", StringComparison.Ordinal) >= 0 ||
            source.IndexOf(" class ", StringComparison.Ordinal) >= 0 ||
            source.IndexOf(" struct ", StringComparison.Ordinal) >= 0 ||
            source.IndexOf(" interface ", StringComparison.Ordinal) >= 0 ||
            source.IndexOf(" enum ", StringComparison.Ordinal) >= 0 ||
            source.IndexOf("record ", StringComparison.Ordinal) >= 0;

        static List<object> BuildOutlineMembers(IEnumerable<MemberDeclarationSyntax> members, bool includeInvocations) =>
            members.Select(a => BuildOutlineNode(a, includeInvocations))
                .Where(a => a != null)
                .Cast<object>()
                .ToList();

        static Dictionary<string, object?>? BuildOutlineNode(MemberDeclarationSyntax member, bool includeInvocations)
        {
            switch (member)
            {
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    return new Dictionary<string, object?> {
                        ["kind"] = "namespace",
                        ["name"] = namespaceDeclaration.Name.ToString(),
                        ["members"] = BuildOutlineMembers(namespaceDeclaration.Members, includeInvocations)
                    };

                case FileScopedNamespaceDeclarationSyntax fileScopedNamespace:
                    return new Dictionary<string, object?> {
                        ["kind"] = "namespace",
                        ["name"] = fileScopedNamespace.Name.ToString(),
                        ["members"] = BuildOutlineMembers(fileScopedNamespace.Members, includeInvocations)
                    };

                case BaseTypeDeclarationSyntax baseTypeDeclaration:
                    return BuildTypeOutline(baseTypeDeclaration, includeInvocations);

                case DelegateDeclarationSyntax delegateDeclaration:
                    return new Dictionary<string, object?> {
                        ["kind"] = "delegate",
                        ["name"] = delegateDeclaration.Identifier.ValueText,
                        ["return_type"] = delegateDeclaration.ReturnType.ToString(),
                        ["parameters"] = delegateDeclaration.ParameterList.Parameters.Select(BuildParameterOutline).ToList()
                    };

                case GlobalStatementSyntax globalStatement:
                    return new Dictionary<string, object?> {
                        ["kind"] = "global_statement",
                        ["summary"] = globalStatement.Statement.Kind().ToString()
                    };

                default:
                    return null;
            }
        }

        static Dictionary<string, object?> BuildTypeOutline(BaseTypeDeclarationSyntax type, bool includeInvocations)
        {
            var kind = type.Kind() switch
            {
                SyntaxKind.ClassDeclaration => "class",
                SyntaxKind.StructDeclaration => "struct",
                SyntaxKind.InterfaceDeclaration => "interface",
                SyntaxKind.EnumDeclaration => "enum",
                SyntaxKind.RecordDeclaration => "record",
                _ => "type"
            };

            IEnumerable<MemberDeclarationSyntax> members = type switch
            {
                TypeDeclarationSyntax typeDeclaration => typeDeclaration.Members,
                EnumDeclarationSyntax enumDeclaration => enumDeclaration.Members.Cast<MemberDeclarationSyntax>().ToList(),
                _ => new List<MemberDeclarationSyntax>()
            };

            return new Dictionary<string, object?> {
                ["kind"] = kind,
                ["name"] = type switch {
                    TypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
                    EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.ValueText,
                    _ => type.ToString()
                },
                ["modifiers"] = GetModifierText(type.Modifiers),
                ["base_types"] = type.BaseList?.Types.Select(a => a.Type.ToString()).ToList() ?? new List<string>(),
                ["members"] = members.Select(a => BuildMemberOutline(a, includeInvocations)).Where(a => a != null).Cast<object>().ToList()
            };
        }

        static Dictionary<string, object?>? BuildMemberOutline(MemberDeclarationSyntax member, bool includeInvocations)
        {
            switch (member)
            {
                case BaseTypeDeclarationSyntax nestedType:
                    return BuildTypeOutline(nestedType, includeInvocations);

                case MethodDeclarationSyntax method:
                    return new Dictionary<string, object?> {
                        ["kind"] = "method",
                        ["name"] = method.Identifier.ValueText,
                        ["return_type"] = method.ReturnType.ToString(),
                        ["modifiers"] = GetModifierText(method.Modifiers),
                        ["parameters"] = method.ParameterList.Parameters.Select(BuildParameterOutline).ToList(),
                        ["body"] = DescribeExecutableBody(method.Body, method.ExpressionBody, includeInvocations)
                    };

                case ConstructorDeclarationSyntax constructor:
                    return new Dictionary<string, object?> {
                        ["kind"] = "constructor",
                        ["name"] = constructor.Identifier.ValueText,
                        ["modifiers"] = GetModifierText(constructor.Modifiers),
                        ["parameters"] = constructor.ParameterList.Parameters.Select(BuildParameterOutline).ToList(),
                        ["body"] = DescribeExecutableBody(constructor.Body, constructor.ExpressionBody, includeInvocations)
                    };

                case DestructorDeclarationSyntax destructor:
                    return new Dictionary<string, object?> {
                        ["kind"] = "destructor",
                        ["name"] = destructor.Identifier.ValueText,
                        ["body"] = DescribeExecutableBody(destructor.Body, destructor.ExpressionBody, includeInvocations)
                    };

                case PropertyDeclarationSyntax property:
                    return new Dictionary<string, object?> {
                        ["kind"] = "property",
                        ["name"] = property.Identifier.ValueText,
                        ["type"] = property.Type.ToString(),
                        ["modifiers"] = GetModifierText(property.Modifiers),
                        ["accessors"] = property.AccessorList?.Accessors.Select(a => a.Keyword.ValueText).ToList() ?? new List<string>(),
                        ["has_expression_body"] = property.ExpressionBody != null
                    };

                case IndexerDeclarationSyntax indexer:
                    return new Dictionary<string, object?> {
                        ["kind"] = "indexer",
                        ["type"] = indexer.Type.ToString(),
                        ["parameters"] = indexer.ParameterList.Parameters.Select(BuildParameterOutline).ToList(),
                        ["accessors"] = indexer.AccessorList?.Accessors.Select(a => a.Keyword.ValueText).ToList() ?? new List<string>()
                    };

                case EventDeclarationSyntax eventDeclaration:
                    return new Dictionary<string, object?> {
                        ["kind"] = "event",
                        ["name"] = eventDeclaration.Identifier.ValueText,
                        ["type"] = eventDeclaration.Type.ToString(),
                        ["accessors"] = eventDeclaration.AccessorList?.Accessors.Select(a => a.Keyword.ValueText).ToList() ?? new List<string>()
                    };

                case EventFieldDeclarationSyntax eventField:
                    return new Dictionary<string, object?> {
                        ["kind"] = "event_field",
                        ["type"] = eventField.Declaration.Type.ToString(),
                        ["names"] = eventField.Declaration.Variables.Select(a => a.Identifier.ValueText).ToList()
                    };

                case FieldDeclarationSyntax field:
                    return new Dictionary<string, object?> {
                        ["kind"] = "field",
                        ["type"] = field.Declaration.Type.ToString(),
                        ["modifiers"] = GetModifierText(field.Modifiers),
                        ["names"] = field.Declaration.Variables.Select(a => a.Identifier.ValueText).ToList()
                    };

                case EnumMemberDeclarationSyntax enumMember:
                    return new Dictionary<string, object?> {
                        ["kind"] = "enum_member",
                        ["name"] = enumMember.Identifier.ValueText
                    };

                default:
                    return null;
            }
        }

        static Dictionary<string, object?> BuildParameterOutline(ParameterSyntax parameter) =>
            new Dictionary<string, object?> {
                ["name"] = parameter.Identifier.ValueText,
                ["type"] = parameter.Type?.ToString(),
                ["modifier"] = parameter.Modifiers.ToString().Trim()
            };

        static Dictionary<string, object?> DescribeExecutableBody(BlockSyntax? block, ArrowExpressionClauseSyntax? expressionBody, bool includeInvocations)
        {
            var node = (SyntaxNode?)block ?? expressionBody?.Expression;
            var invocationCount = includeInvocations && node != null
                ? node.DescendantNodes().OfType<InvocationExpressionSyntax>().Count()
                : 0;
            var objectCreationCount = includeInvocations && node != null
                ? node.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Count()
                : 0;

            return new Dictionary<string, object?> {
                ["statement_count"] = block?.Statements.Count ?? (expressionBody != null ? 1 : 0),
                ["has_expression_body"] = expressionBody != null,
                ["invocation_count"] = includeInvocations ? invocationCount : null,
                ["object_creation_count"] = includeInvocations ? objectCreationCount : null
            };
        }

        static string GetModifierText(SyntaxTokenList modifiers) =>
            string.Join(" ", modifiers.Select(a => a.Text));

        static Dictionary<string, object?> DescribeEntryPoint(MethodDef? method)
        {
            if (method == null)
                return new Dictionary<string, object?> {
                    ["exists"] = false
                };

            return new Dictionary<string, object?> {
                ["exists"] = true,
                ["type_full_name"] = method.DeclaringType?.FullName,
                ["method_name"] = method.Name.String,
                ["signature"] = method.MethodSig?.ToString(),
                ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M"
            };
        }

        static bool HasModuleInitializerAttribute(MethodDef method) =>
            method.CustomAttributes.Any(a =>
                string.Equals(a.AttributeType.FullName, "System.Runtime.CompilerServices.ModuleInitializerAttribute", StringComparison.Ordinal));

        static bool IsLikelyStartupMethod(MethodDef method)
        {
            if (method.IsStaticConstructor || HasModuleInitializerAttribute(method))
                return true;

            var name = method.Name.String;
            if (string.Equals(name, "Main", StringComparison.Ordinal) ||
                string.Equals(name, "OnStartup", StringComparison.Ordinal) ||
                string.Equals(name, "InitializeComponent", StringComparison.Ordinal) ||
                string.Equals(name, "Startup", StringComparison.Ordinal))
                return true;

            return method.CustomAttributes.Any(a =>
                string.Equals(a.AttributeType.FullName, "System.STAThreadAttribute", StringComparison.Ordinal) ||
                string.Equals(a.AttributeType.FullName, "System.MTAThreadAttribute", StringComparison.Ordinal));
        }

        static string ClassifyResource(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".baml"))
                return "baml";
            if (lower.EndsWith(".xaml"))
                return "xaml";
            if (lower.EndsWith(".resources") || lower.EndsWith(".resx"))
                return "resx";
            if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".bmp") || lower.EndsWith(".dds") || lower.EndsWith(".tga"))
                return "image";
            if (IsAssemblyLikeResource(lower))
                return "assembly";
            if (lower.EndsWith(".xml") || lower.EndsWith(".json") || lower.EndsWith(".txt") || lower.EndsWith(".ini"))
                return "data";
            return "unknown";
        }

        static bool IsAssemblyLikeResource(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.EndsWith(".dll") || lower.EndsWith(".exe") ||
                   lower.EndsWith(".dll.compressed") || lower.EndsWith(".exe.compressed");
        }

        IEnumerable<AssemblyDef> ResolveAssembliesForSearch(Dictionary<string, object>? arguments)
        {
            if (arguments != null && (arguments.ContainsKey("assembly_name") || arguments.ContainsKey("file_path")))
                return new[] { ResolveAssembly(arguments) };

            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(a => a.Document?.AssemblyDef)
                    .Where(a => a != null)
                    .Distinct()
                    .ToList())!;
        }

        static IEnumerable<Dictionary<string, object?>> SearchMembersInAssembly(AssemblyDef assembly, System.Text.RegularExpressions.Regex regex, string? kindFilter)
        {
            foreach (var type in assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)))
            {
                if (kindFilter == null || kindFilter == "type")
                {
                    if (regex.IsMatch(type.Name.String) || regex.IsMatch(type.FullName))
                        yield return BuildMemberPayload(type.Module, type.MDToken.Raw, 'T', new Dictionary<string, object?> {
                            ["assembly_name"] = type.Module.Assembly?.Name.String,
                            ["type_full_name"] = type.FullName,
                            ["name"] = type.Name.String,
                            ["full_name"] = type.FullName,
                            ["kind"] = "type"
                        });
                }

                if (kindFilter == null || kindFilter == "method")
                {
                    foreach (var method in type.Methods.Where(a => regex.IsMatch(a.Name.String) || regex.IsMatch(a.FullName)))
                    {
                        yield return BuildMemberPayload(method.Module, method.MDToken.Raw, 'M', new Dictionary<string, object?> {
                            ["assembly_name"] = method.Module.Assembly?.Name.String,
                            ["type_full_name"] = method.DeclaringType?.FullName,
                            ["name"] = method.Name.String,
                            ["full_name"] = method.FullName,
                            ["kind"] = "method"
                        });
                    }
                }

                if (kindFilter == null || kindFilter == "field")
                {
                    foreach (var field in type.Fields.Where(a => regex.IsMatch(a.Name.String) || regex.IsMatch(a.FullName)))
                    {
                        yield return BuildMemberPayload(field.Module, field.MDToken.Raw, 'F', new Dictionary<string, object?> {
                            ["assembly_name"] = field.Module.Assembly?.Name.String,
                            ["type_full_name"] = field.DeclaringType?.FullName,
                            ["name"] = field.Name.String,
                            ["full_name"] = field.FullName,
                            ["kind"] = "field"
                        });
                    }
                }

                if (kindFilter == null || kindFilter == "property")
                {
                    foreach (var property in type.Properties.Where(a => regex.IsMatch(a.Name.String) || regex.IsMatch(a.FullName)))
                    {
                        yield return BuildMemberPayload(property.Module, property.MDToken.Raw, 'P', new Dictionary<string, object?> {
                            ["assembly_name"] = property.Module.Assembly?.Name.String,
                            ["type_full_name"] = property.DeclaringType?.FullName,
                            ["name"] = property.Name.String,
                            ["full_name"] = property.FullName,
                            ["kind"] = "property"
                        });
                    }
                }

                if (kindFilter == null || kindFilter == "event")
                {
                    foreach (var @event in type.Events.Where(a => regex.IsMatch(a.Name.String) || regex.IsMatch(a.FullName)))
                    {
                        yield return BuildMemberPayload(@event.Module, @event.MDToken.Raw, 'E', new Dictionary<string, object?> {
                            ["assembly_name"] = @event.Module.Assembly?.Name.String,
                            ["type_full_name"] = @event.DeclaringType?.FullName,
                            ["name"] = @event.Name.String,
                            ["full_name"] = @event.FullName,
                            ["kind"] = "event"
                        });
                    }
                }
            }
        }

        static IEnumerable<(string Value, MethodDef Method)> EnumerateStringLiteralOccurrences(AssemblyDef assembly)
        {
            foreach (var method in assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)).SelectMany(a => a.Methods))
            {
                if (method.Body?.Instructions == null)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && instruction.Operand is string value)
                        yield return (value, method);
                }
            }
        }

        static bool IsReflectionLikeMethod(IMethod method)
        {
            var declaringType = method.DeclaringType?.FullName ?? string.Empty;
            var fullName = method.FullName ?? string.Empty;

            if (declaringType.StartsWith("System.Reflection.", StringComparison.Ordinal))
                return true;

            if (declaringType == "System.Type" ||
                declaringType == "System.Activator" ||
                declaringType == "System.AppDomain" ||
                declaringType == "System.RuntimeTypeHandle" ||
                declaringType == "System.RuntimeMethodHandle")
                return true;

            return fullName.Contains("::GetType(", StringComparison.Ordinal) ||
                   fullName.Contains("::Invoke(", StringComparison.Ordinal) ||
                   fullName.Contains("::CreateInstance(", StringComparison.Ordinal) ||
                   fullName.Contains("::GetMethod(", StringComparison.Ordinal) ||
                   fullName.Contains("::GetField(", StringComparison.Ordinal) ||
                   fullName.Contains("::GetProperty(", StringComparison.Ordinal) ||
                   fullName.Contains("::Load(", StringComparison.Ordinal);
        }

        static string ClassifyReflectionUsage(IMethod method)
        {
            var fullName = method.FullName ?? string.Empty;
            if (fullName.Contains("::Invoke(", StringComparison.Ordinal))
                return "invoke";
            if (fullName.Contains("::CreateInstance(", StringComparison.Ordinal))
                return "activation";
            if (fullName.Contains("::GetType(", StringComparison.Ordinal) || fullName.Contains("::Load(", StringComparison.Ordinal))
                return "type_or_assembly_lookup";
            if (fullName.Contains("::GetMethod(", StringComparison.Ordinal) ||
                fullName.Contains("::GetField(", StringComparison.Ordinal) ||
                fullName.Contains("::GetProperty(", StringComparison.Ordinal))
                return "member_lookup";
            return "reflection";
        }

        static Dictionary<string, object?>? DescribeAntiDebugHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            var evidence = new List<string>();
            var calledApis = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();
            var stringLiterals = method.Body.Instructions
                .Where(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && a.Operand is string)
                .Select(a => a.Operand!.ToString()!)
                .ToList();

            if (calledApis.Any(a => a.Contains("System.Boolean System.Diagnostics.Debugger::get_IsAttached()", StringComparison.Ordinal)))
                evidence.Add("Calls Debugger.get_IsAttached()");
            if (calledApis.Any(a => a.Contains("System.Void System.Diagnostics.Process::EnterDebugMode()", StringComparison.Ordinal)))
                evidence.Add("Calls Process.EnterDebugMode()");
            if (calledApis.Any(a => a.Contains("System.Void System.Environment::FailFast(System.String)", StringComparison.Ordinal)))
                evidence.Add("Calls Environment.FailFast()");

            var pinvokeNames = method.DeclaringType?.Methods
                .Where(a => a.HasImplMap)
                .Select(a => $"{a.ImplMap?.Module?.Name?.String}!{a.ImplMap?.Name?.String ?? a.Name.String}")
                .ToList() ?? new List<string>();

            if (pinvokeNames.Any(a => a.IndexOf("IsDebuggerPresent", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Declaring type imports IsDebuggerPresent");
            if (pinvokeNames.Any(a => a.IndexOf("CheckRemoteDebuggerPresent", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Declaring type imports CheckRemoteDebuggerPresent");
            if (pinvokeNames.Any(a => a.IndexOf("NtQueryInformationProcess", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Declaring type imports NtQueryInformationProcess");
            if (pinvokeNames.Any(a => a.IndexOf("NtSetInformationProcess", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Declaring type imports NtSetInformationProcess");

            if (stringLiterals.Any(a => a.IndexOf("Debugger detected", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Contains debugger-detection string literal");
            if (stringLiterals.Any(a => a.IndexOf("COR_ENABLE_PROFILING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        a.IndexOf("COR_PROFILER", StringComparison.OrdinalIgnoreCase) >= 0))
                evidence.Add("Contains profiler anti-debug environment strings");

            if (evidence.Count == 0)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "anti_debug";
            payload["confidence"] = ComputeConfidence(evidence.Count + (method.IsStatic ? 1 : 0));
            payload["evidence"] = evidence;
            return payload;
        }

        static Dictionary<string, object?>? DescribeAntiTamperHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            var calledApis = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();
            var evidence = new List<string>();

            if (calledApis.Any(a => a.Contains("System.Reflection.Assembly System.Reflection.Assembly::GetExecutingAssembly()", StringComparison.Ordinal)))
                evidence.Add("Reads executing assembly identity");
            if (calledApis.Any(a => a.Contains("System.String System.Reflection.Assembly::get_Location()", StringComparison.Ordinal)))
                evidence.Add("Reads current assembly location");
            if (calledApis.Any(a => a.Contains("System.Byte[] System.IO.File::ReadAllBytes(System.String)", StringComparison.Ordinal)))
                evidence.Add("Reads assembly bytes from disk");
            if (calledApis.Any(a => a.Contains("System.Security.Cryptography.MD5", StringComparison.Ordinal) ||
                                    a.Contains("System.Security.Cryptography.SHA1", StringComparison.Ordinal) ||
                                    a.Contains("System.Security.Cryptography.SHA256", StringComparison.Ordinal)))
                evidence.Add("Uses hashing API");
            if (calledApis.Any(a => a.Contains("System.Byte[] System.Security.Cryptography.HashAlgorithm::ComputeHash(System.Byte[])", StringComparison.Ordinal)))
                evidence.Add("Computes hash over byte data");
            if (calledApis.Any(a => a.Contains("System.Void System.Environment::FailFast(System.String)", StringComparison.Ordinal)))
                evidence.Add("Can terminate process on integrity failure");

            if (evidence.Count < 3)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "anti_tamper";
            payload["confidence"] = ComputeConfidence(evidence.Count);
            payload["evidence"] = evidence;
            return payload;
        }

        static Dictionary<string, object?>? DescribeProxyMethodHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            var instructions = method.Body.Instructions;
            var evidence = new List<string>();
            var calledTargets = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();

            if (instructions.Count <= 8 &&
                instructions.Count(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Call || a.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt || a.OpCode.Code == dnlib.DotNet.Emit.Code.Newobj) == 1 &&
                instructions.All(a => IsSimpleProxyInstruction(a.OpCode.Code)))
                evidence.Add("Method body is a tiny forwarding wrapper");

            if (instructions.Any(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Ldftn) &&
                instructions.Any(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Newobj))
                evidence.Add("Builds delegate via ldftn + newobj");

            if (calledTargets.Any(a => a.Contains("System.Reflection.Emit.DynamicMethod::.ctor", StringComparison.Ordinal)))
                evidence.Add("Creates DynamicMethod");

            if (calledTargets.Any(a => a.Contains("System.Delegate::CreateDelegate", StringComparison.Ordinal)))
                evidence.Add("Uses Delegate.CreateDelegate");

            if (evidence.Count == 0)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "proxy_method";
            payload["confidence"] = ComputeConfidence(evidence.Count + (instructions.Count <= 8 ? 2 : 0));
            payload["evidence"] = evidence;
            payload["called_targets"] = calledTargets.Distinct(StringComparer.Ordinal).Take(5).ToList();
            return payload;
        }

        static Dictionary<string, object?>? DescribeStringDecryptorHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            if (!string.Equals(method.ReturnType.FullName, "System.String", StringComparison.Ordinal))
                return null;

            var evidence = new List<string>();
            var calledTargets = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();

            if (calledTargets.Any(a => a.Contains("System.Byte[] System.Convert::FromBase64String(System.String)", StringComparison.Ordinal)))
                evidence.Add("Calls Convert.FromBase64String()");
            if (calledTargets.Any(a => a.Contains("System.String System.Text.Encoding::GetString", StringComparison.Ordinal) ||
                                       a.Contains("System.String System.Text.UTF8Encoding::GetString", StringComparison.Ordinal) ||
                                       a.Contains("System.String System.Text.UnicodeEncoding::GetString", StringComparison.Ordinal)))
                evidence.Add("Decodes bytes with text encoding");
            if (calledTargets.Any(a => a.Contains("System.Char[] System.String::ToCharArray()", StringComparison.Ordinal)))
                evidence.Add("Converts string to char array");
            if (calledTargets.Any(a => a.Contains("System.String System.String::Intern(System.String)", StringComparison.Ordinal)))
                evidence.Add("Interns resulting string");
            if (calledTargets.Any(a => a.Contains("System.Security.Cryptography.", StringComparison.Ordinal)))
                evidence.Add("Uses cryptography API");

            if (method.Body.Instructions.Any(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Xor))
                evidence.Add("Uses XOR in IL body");

            var parameterTypes = method.Parameters.Select(a => a.Type.FullName).ToList();
            if (parameterTypes.Any(a => a == "System.Int32" || a == "System.UInt32" || a == "System.String"))
                evidence.Add("Has common string-decryptor parameter shape");

            var callerCount = FindCallersForMethodStatic(method).Count();
            if (callerCount >= 3)
                evidence.Add($"Called from {callerCount} methods");

            if (evidence.Count == 0)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "string_decryptor";
            payload["confidence"] = ComputeConfidence(evidence.Count + (callerCount >= 3 ? 2 : 0));
            payload["evidence"] = evidence;
            payload["caller_count"] = callerCount;
            payload["parameter_types"] = parameterTypes;
            return payload;
        }

        static Dictionary<string, object?>? DescribeDelegateCreationHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            var evidence = new List<string>();
            var calledTargets = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();
            if (calledTargets.Any(a => a.Contains("System.Delegate System.Delegate::CreateDelegate", StringComparison.Ordinal)))
                evidence.Add("Calls Delegate.CreateDelegate()");
            if (calledTargets.Any(a => a.Contains("System.MulticastDelegate", StringComparison.Ordinal)))
                evidence.Add("Interacts with multicast delegate API");
            if (method.Body.Instructions.Any(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Ldftn))
                evidence.Add("Uses ldftn");
            if (method.Body.Instructions.Any(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Newobj &&
                                                  a.Operand is IMethod ctor &&
                                                  IsDelegateType(ctor.DeclaringType?.ResolveTypeDef())))
                evidence.Add("Constructs delegate instance");

            if (evidence.Count == 0)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "delegate_creation";
            payload["confidence"] = ComputeConfidence(evidence.Count);
            payload["evidence"] = evidence;
            return payload;
        }

        static Dictionary<string, object?>? DescribeDynamicCodeHit(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                return null;

            var calledTargets = EnumerateCalledMethods(method).Select(a => a.FullName ?? string.Empty).ToList();
            var evidence = new List<string>();

            if (calledTargets.Any(a => a.Contains("System.Reflection.Emit.DynamicMethod::.ctor", StringComparison.Ordinal)))
                evidence.Add("Creates DynamicMethod");
            if (calledTargets.Any(a => a.Contains("System.Reflection.Emit.ILGenerator::Emit", StringComparison.Ordinal)))
                evidence.Add("Emits IL at runtime");
            if (calledTargets.Any(a => a.Contains("System.Linq.Expressions.Expression`1::Compile", StringComparison.Ordinal) ||
                                       a.Contains("System.Linq.Expressions.LambdaExpression::Compile", StringComparison.Ordinal)))
                evidence.Add("Compiles expression tree");
            if (calledTargets.Any(a => a.Contains("System.Reflection.Assembly::Load", StringComparison.Ordinal) ||
                                       a.Contains("System.Reflection.Assembly::LoadFile", StringComparison.Ordinal) ||
                                       a.Contains("System.Reflection.Assembly::LoadFrom", StringComparison.Ordinal)))
                evidence.Add("Loads assembly dynamically");

            if (evidence.Count == 0)
                return null;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["heuristic"] = "dynamic_code";
            payload["confidence"] = ComputeConfidence(evidence.Count);
            payload["evidence"] = evidence;
            return payload;
        }

        IEnumerable<Dictionary<string, object?>> DescribeByteArrayHits(AssemblyDef assembly)
        {
            foreach (var field in assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)).SelectMany(a => a.Fields))
            {
                if (!string.Equals(field.FieldType.FullName, "System.Byte[]", StringComparison.Ordinal))
                    continue;

                yield return BuildMemberPayload(field.Module, field.MDToken.Raw, 'F', new Dictionary<string, object?> {
                    ["assembly_name"] = field.Module.Assembly?.Name.String,
                    ["name"] = field.Name.String,
                    ["full_name"] = field.FullName,
                    ["declaring_type"] = field.DeclaringType?.FullName,
                    ["usage_kind"] = "byte_array_field"
                });
            }

            foreach (var method in EnumerateAllMethods(assembly))
            {
                if (method.Body?.Instructions == null)
                    continue;

                int newarrCount = method.Body.Instructions.Count(a =>
                    a.OpCode.Code == dnlib.DotNet.Emit.Code.Newarr &&
                    a.Operand is ITypeDefOrRef typeRef &&
                    string.Equals(typeRef.FullName, "System.Byte", StringComparison.Ordinal));
                if (newarrCount == 0)
                    continue;

                var payload = DescribeReferenceWithMemberIdStatic(method);
                payload["usage_kind"] = "byte_array_allocation";
                payload["newarr_count"] = newarrCount;
                yield return payload;
            }
        }

        IEnumerable<Dictionary<string, object?>> DescribeEmbeddedPeHits(AssemblyDef assembly)
        {
            foreach (var resource in assembly.ManifestModule?.Resources ?? Enumerable.Empty<Resource>())
            {
                if (resource is not EmbeddedResource embedded)
                    continue;

                byte[] bytes;
                try
                {
                    bytes = embedded.CreateReader().ToArray();
                }
                catch
                {
                    continue;
                }

                bool looksLikePe = bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';
                bool looksCompressedAssembly = resource.Name.String.EndsWith(".dll.compressed", StringComparison.OrdinalIgnoreCase) ||
                                              resource.Name.String.EndsWith(".exe.compressed", StringComparison.OrdinalIgnoreCase);
                bool assemblyNamed = resource.Name.String.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                     resource.Name.String.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                if (!looksLikePe && !looksCompressedAssembly && !assemblyNamed)
                    continue;

                yield return new Dictionary<string, object?> {
                    ["assembly_name"] = assembly.Name.String,
                    ["resource_name"] = resource.Name.String,
                    ["size"] = bytes.Length,
                    ["looks_like_pe"] = looksLikePe,
                    ["looks_compressed_assembly"] = looksCompressedAssembly,
                    ["resource_kind"] = resource.GetType().Name
                };
            }
        }

        static Dictionary<string, object?> DescribeControlFlowSummary(MethodDef method)
        {
            var instructions = method.Body?.Instructions ?? throw new ArgumentException("Method body required");
            int branchCount = instructions.Count(a => a.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Branch ||
                                                      a.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Cond_Branch);
            int switchCount = instructions.Count(a => a.OpCode.Code == dnlib.DotNet.Emit.Code.Switch);
            int exceptionHandlerCount = method.Body?.ExceptionHandlers?.Count ?? 0;
            bool flattenedLike = switchCount > 0 && branchCount >= 8;

            var payload = DescribeReferenceWithMemberIdStatic(method);
            payload["instruction_count"] = instructions.Count;
            payload["branch_count"] = branchCount;
            payload["switch_count"] = switchCount;
            payload["exception_handler_count"] = exceptionHandlerCount;
            payload["heuristic_flags"] = flattenedLike
                ? new List<string> { "possible_flattening" }
                : new List<string>();
            return payload;
        }

        Dictionary<string, object?> BuildControlFlowGraph(MethodDef method, bool includeInstructions)
        {
            var instructions = method.Body?.Instructions ?? throw new ArgumentException("Method body required");
            var leaders = new HashSet<Instruction> { instructions[0] };

            foreach (var handler in method.Body?.ExceptionHandlers ?? Enumerable.Empty<ExceptionHandler>())
            {
                if (handler.TryStart != null)
                    leaders.Add(handler.TryStart);
                if (handler.HandlerStart != null)
                    leaders.Add(handler.HandlerStart);
                if (handler.FilterStart != null)
                    leaders.Add(handler.FilterStart);
            }

            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                foreach (var target in EnumerateBranchTargets(instruction))
                    leaders.Add(target);

                if (i + 1 < instructions.Count && EndsBasicBlock(instruction))
                    leaders.Add(instructions[i + 1]);
            }

            var orderedLeaders = instructions
                .Where(leaders.Contains)
                .OrderBy(a => a.Offset)
                .ToList();
            var blockStartOffsets = new HashSet<uint>(orderedLeaders.Select(a => a.Offset));
            var blocks = new List<List<Instruction>>();
            List<Instruction>? currentBlock = null;

            foreach (var instruction in instructions)
            {
                if (blockStartOffsets.Contains(instruction.Offset))
                {
                    currentBlock = new List<Instruction>();
                    blocks.Add(currentBlock);
                }

                currentBlock ??= new List<Instruction>();
                if (!blocks.Contains(currentBlock))
                    blocks.Add(currentBlock);
                currentBlock.Add(instruction);
            }

            var offsetToBlockId = new Dictionary<uint, string>();
            var blockPayloads = new List<Dictionary<string, object?>>();
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                string blockId = $"B{i}";
                offsetToBlockId[block[0].Offset] = blockId;

                var payload = new Dictionary<string, object?> {
                    ["block_id"] = blockId,
                    ["start_offset"] = $"IL_{block[0].Offset:X4}",
                    ["end_offset"] = $"IL_{block[^1].Offset:X4}",
                    ["instruction_count"] = block.Count,
                    ["last_opcode"] = block[^1].OpCode.Name
                };

                if (includeInstructions)
                {
                    payload["instructions"] = block.Select(a => new Dictionary<string, object?> {
                        ["offset"] = $"IL_{a.Offset:X4}",
                        ["opcode"] = a.OpCode.Name,
                        ["operand"] = FormatInstructionOperand(a.Operand)
                    }).ToList();
                }

                blockPayloads.Add(payload);
            }

            var edges = new List<Dictionary<string, object?>>();
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var lastInstruction = block[^1];
                var fromId = $"B{i}";
                var targetInstructions = EnumerateBranchTargets(lastInstruction).ToList();

                if (lastInstruction.OpCode.Code == dnlib.DotNet.Emit.Code.Switch)
                {
                    foreach (var target in targetInstructions)
                    {
                        if (offsetToBlockId.TryGetValue(target.Offset, out var toId))
                        {
                            edges.Add(new Dictionary<string, object?> {
                                ["from"] = fromId,
                                ["to"] = toId,
                                ["kind"] = "switch"
                            });
                        }
                    }
                }
                else if (lastInstruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Branch ||
                         lastInstruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Cond_Branch)
                {
                    foreach (var target in targetInstructions)
                    {
                        if (offsetToBlockId.TryGetValue(target.Offset, out var toId))
                        {
                            edges.Add(new Dictionary<string, object?> {
                                ["from"] = fromId,
                                ["to"] = toId,
                                ["kind"] = lastInstruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Branch ? "branch" : "cond_branch"
                            });
                        }
                    }
                }

                if (HasFallthrough(lastInstruction) && i + 1 < blocks.Count)
                {
                    edges.Add(new Dictionary<string, object?> {
                        ["from"] = fromId,
                        ["to"] = $"B{i + 1}",
                        ["kind"] = "fallthrough"
                    });
                }
            }

            return new Dictionary<string, object?> {
                ["target"] = DescribeReferenceWithMemberId(method),
                ["block_count"] = blockPayloads.Count,
                ["edge_count"] = edges.Count,
                ["blocks"] = blockPayloads,
                ["edges"] = edges
            };
        }

        static IEnumerable<Instruction> EnumerateBranchTargets(Instruction instruction)
        {
            if (instruction.Operand is Instruction target)
            {
                yield return target;
                yield break;
            }

            if (instruction.Operand is IList<Instruction> targets)
            {
                foreach (var item in targets)
                    yield return item;
            }
        }

        static bool EndsBasicBlock(Instruction instruction) =>
            instruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Branch ||
            instruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Cond_Branch ||
            instruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Return ||
            instruction.OpCode.FlowControl == dnlib.DotNet.Emit.FlowControl.Throw;

        static bool HasFallthrough(Instruction instruction) =>
            instruction.OpCode.FlowControl != dnlib.DotNet.Emit.FlowControl.Branch &&
            instruction.OpCode.FlowControl != dnlib.DotNet.Emit.FlowControl.Return &&
            instruction.OpCode.FlowControl != dnlib.DotNet.Emit.FlowControl.Throw;

        static string? FormatInstructionOperand(object? operand)
        {
            if (operand == null)
                return null;
            if (operand is Instruction target)
                return $"IL_{target.Offset:X4}";
            if (operand is IList<Instruction> targets)
                return string.Join(", ", targets.Select(a => $"IL_{a.Offset:X4}"));
            return operand.ToString();
        }

        List<Dictionary<string, object?>> BuildFrameworkOrPackageMatches(AssemblyDef assembly)
        {
            var rules = GetKnownComponentRules();
            var referenceNames = assembly.Modules.SelectMany(a => a.GetAssemblyRefs())
                .Select(a => a.Name.String)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var namespaceNames = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .Select(a => a.Namespace.String)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var items = new List<Dictionary<string, object?>>();
            foreach (var rule in rules)
            {
                var evidence = new List<string>();
                evidence.AddRange(referenceNames.Where(a => MatchesKnownComponent(a, rule)).Select(a => $"assembly_ref:{a}"));
                evidence.AddRange(namespaceNames.Where(a => MatchesKnownComponent(a, rule)).Take(8).Select(a => $"namespace:{a}"));
                if (evidence.Count == 0)
                    continue;

                items.Add(new Dictionary<string, object?> {
                    ["name"] = rule.Name,
                    ["kind"] = rule.Kind,
                    ["confidence"] = Math.Min(0.98, rule.Confidence + Math.Min(0.1, evidence.Count * 0.02)),
                    ["evidence"] = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            return items;
        }

        List<Dictionary<string, object?>> BuildThirdPartyComponentLabels(AssemblyDef assembly)
        {
            var rules = GetKnownComponentRules();
            var refs = assembly.Modules.SelectMany(a => a.GetAssemblyRefs())
                .Select(a => a.Name.String)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var namespaces = assembly.Modules
                .SelectMany(a => GetAllTypesRecursive(a.Types))
                .Select(a => a.Namespace.String)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var components = new List<Dictionary<string, object?>>();
            foreach (var referenceName in refs)
            {
                foreach (var rule in rules.Where(a => MatchesKnownComponent(referenceName, a)))
                {
                    components.Add(new Dictionary<string, object?> {
                        ["component_name"] = rule.Name,
                        ["component_kind"] = rule.Kind,
                        ["source_kind"] = "assembly_reference",
                        ["source_name"] = referenceName,
                        ["confidence"] = rule.Confidence
                    });
                }
            }

            foreach (var namespaceName in namespaces)
            {
                foreach (var rule in rules.Where(a => MatchesKnownComponent(namespaceName, a)))
                {
                    components.Add(new Dictionary<string, object?> {
                        ["component_name"] = rule.Name,
                        ["component_kind"] = rule.Kind,
                        ["source_kind"] = "namespace",
                        ["source_name"] = namespaceName,
                        ["confidence"] = Math.Max(0.55, rule.Confidence - 0.05)
                    });
                }
            }

            return components
                .GroupBy(a => $"{a["component_name"]}|{a["component_kind"]}|{a["source_kind"]}|{a["source_name"]}", StringComparer.OrdinalIgnoreCase)
                .Select(a => a.OrderByDescending(b => b.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d).First())
                .OrderByDescending(a => a.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d)
                .ThenBy(a => a["component_name"]?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        Dictionary<string, object?> IdentifyKnownBinaryCore(AssemblyDef assembly)
        {
            var publicKeyToken = assembly.PublicKeyToken?.ToString() ?? "null";
            foreach (var rule in GetKnownBinaryRules())
            {
                if (!MatchesKnownBinaryRule(assembly, publicKeyToken, rule))
                    continue;

                return new Dictionary<string, object?> {
                    ["is_known_binary"] = true,
                    ["identity"] = rule.Identity,
                    ["kind"] = rule.Kind,
                    ["confidence"] = rule.Confidence,
                    ["names_trusted"] = rule.NamesTrusted,
                    ["match_reason"] = BuildKnownBinaryReason(assembly, publicKeyToken, rule)
                };
            }

            return new Dictionary<string, object?> {
                ["is_known_binary"] = false,
                ["identity"] = null,
                ["kind"] = "unknown",
                ["confidence"] = 0.25,
                ["names_trusted"] = false,
                ["match_reason"] = "No local framework/package signature rule matched the assembly identity."
            };
        }

        static bool MatchesKnownComponent(string value, KnownComponentRule rule) =>
            value.StartsWith(rule.Prefix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, rule.Prefix, StringComparison.OrdinalIgnoreCase);

        List<Dictionary<string, object?>> BuildSourceCandidates(AssemblyDef assembly)
        {
            var candidates = new List<Dictionary<string, object?>>();

            foreach (var module in assembly.Modules) {
                var status = DescribeSymbolStatus(module);
                if (status.TryGetValue("symbols_loaded", out var loadedObj) && loadedObj is bool loaded && loaded) {
                    candidates.Add(new Dictionary<string, object?> {
                        ["kind"] = "loaded_debug_symbols",
                        ["module_name"] = module.Name.String,
                        ["candidate"] = status.TryGetValue("sidecar_pdb_path", out var pdbPathObj) ? pdbPathObj : module.Location,
                        ["confidence"] = 0.98,
                        ["match_reason"] = $"Debug symbols are already loaded for module '{module.Name.String}'.",
                        ["names_trusted"] = true
                    });
                }

                if (status.TryGetValue("has_sidecar_pdb", out var hasSidecarObj) && hasSidecarObj is bool hasSidecar && hasSidecar) {
                    candidates.Add(new Dictionary<string, object?> {
                        ["kind"] = "sidecar_pdb",
                        ["module_name"] = module.Name.String,
                        ["candidate"] = status.TryGetValue("sidecar_pdb_path", out var sidecarPathObj) ? sidecarPathObj : null,
                        ["confidence"] = 0.95,
                        ["match_reason"] = $"A sidecar PDB exists next to module '{module.Name.String}'.",
                        ["names_trusted"] = true
                    });
                }
            }

            var repositoryUrl = TryGetRepositoryUrl(assembly);
            if (!string.IsNullOrWhiteSpace(repositoryUrl)) {
                candidates.Add(new Dictionary<string, object?> {
                    ["kind"] = "repository_url",
                    ["candidate"] = repositoryUrl,
                    ["confidence"] = 0.85,
                    ["match_reason"] = "Assembly metadata contains a repository URL hint.",
                    ["names_trusted"] = false
                });
            }

            var informationalVersion = TryGetInformationalVersion(assembly);
            if (!string.IsNullOrWhiteSpace(informationalVersion)) {
                candidates.Add(new Dictionary<string, object?> {
                    ["kind"] = "informational_version",
                    ["candidate"] = informationalVersion,
                    ["confidence"] = 0.55,
                    ["match_reason"] = "Assembly informational version may contain a source revision, package version, or build provenance hint.",
                    ["names_trusted"] = false
                });
            }

            var knownBinary = IdentifyKnownBinaryCore(assembly);
            if (knownBinary.TryGetValue("is_known_binary", out var isKnownObj) && isKnownObj is bool isKnownBinary && isKnownBinary) {
                candidates.Add(new Dictionary<string, object?> {
                    ["kind"] = "known_binary",
                    ["candidate"] = knownBinary.TryGetValue("identity", out var identityObj) ? identityObj : assembly.Name.String,
                    ["confidence"] = knownBinary.TryGetValue("confidence", out var confidenceObj) ? confidenceObj : 0.75,
                    ["match_reason"] = knownBinary.TryGetValue("match_reason", out var reasonObj) ? reasonObj : "Known binary rule matched.",
                    ["names_trusted"] = knownBinary.TryGetValue("names_trusted", out var trustedObj) ? trustedObj : false
                });
            }

            candidates.AddRange(BuildFrameworkOrPackageMatches(assembly).Select(a => new Dictionary<string, object?> {
                ["kind"] = "framework_or_package_match",
                ["candidate"] = a.TryGetValue("name", out var nameObj) ? nameObj : null,
                ["confidence"] = a.TryGetValue("confidence", out var confidenceObj) ? confidenceObj : 0.5,
                ["match_reason"] = a.TryGetValue("match_reason", out var reasonObj) ? reasonObj : "Framework or package rule matched.",
                ["names_trusted"] = true
            }));

            return candidates
                .GroupBy(a => $"{a["kind"]}|{a["candidate"]}", StringComparer.OrdinalIgnoreCase)
                .Select(a => a.First())
                .ToList();
        }

        List<Dictionary<string, object?>> BuildOpenSourceCandidates(AssemblyDef assembly)
        {
            var candidates = new List<Dictionary<string, object?>>();
            var repositoryUrl = TryGetRepositoryUrl(assembly);
            if (!string.IsNullOrWhiteSpace(repositoryUrl)) {
                candidates.Add(new Dictionary<string, object?> {
                    ["repository"] = repositoryUrl,
                    ["confidence"] = 0.97,
                    ["match_reason"] = "Assembly metadata contains an explicit repository URL hint.",
                    ["source"] = "assembly_metadata"
                });
            }

            var knownRepositories = GetKnownRepositoryCandidates();
            var namesToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                assembly.Name.String
            };

            foreach (var assemblyRef in assembly.Modules.SelectMany(a => a.GetAssemblyRefs()))
                namesToCheck.Add(assemblyRef.Name.String);

            foreach (var name in namesToCheck) {
                foreach (var candidate in knownRepositories.Where(a =>
                             name.StartsWith(a.NamePrefix, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name, a.NamePrefix, StringComparison.OrdinalIgnoreCase))) {
                    candidates.Add(new Dictionary<string, object?> {
                        ["repository"] = candidate.Repository,
                        ["confidence"] = candidate.Confidence,
                        ["match_reason"] = $"Assembly or reference name '{name}' matched known open-source prefix '{candidate.NamePrefix}'.",
                        ["source"] = "offline_rule"
                    });
                }
            }

            var frameworkMatches = BuildFrameworkOrPackageMatches(assembly);
            foreach (var match in frameworkMatches) {
                var matchName = match.TryGetValue("name", out var nameObj) ? nameObj?.ToString() : null;
                if (string.IsNullOrWhiteSpace(matchName))
                    continue;

                foreach (var candidate in knownRepositories.Where(a =>
                             string.Equals(matchName, a.DisplayName, StringComparison.OrdinalIgnoreCase))) {
                    candidates.Add(new Dictionary<string, object?> {
                        ["repository"] = candidate.Repository,
                        ["confidence"] = Math.Max(candidate.Confidence, match.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d),
                        ["match_reason"] = $"Framework/package match '{matchName}' has a known upstream repository candidate.",
                        ["source"] = "framework_match"
                    });
                }
            }

            return candidates
                .GroupBy(a => a["repository"]?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(a => a.OrderByDescending(b => b.TryGetValue("confidence", out var confidenceObj) ? Convert.ToDouble(confidenceObj) : 0d).First())
                .ToList();
        }

        static IReadOnlyList<KnownComponentRule> GetKnownComponentRules() => new[] {
            new KnownComponentRule("UnityEngine", "game_engine", "Unity", 0.95),
            new KnownComponentRule("Unity.", "game_engine", "Unity", 0.9),
            new KnownComponentRule("TMPro", "ui_framework", "TextMeshPro", 0.9),
            new KnownComponentRule("BepInEx", "modding_framework", "BepInEx", 0.95),
            new KnownComponentRule("HarmonyLib", "patching_framework", "Harmony", 0.95),
            new KnownComponentRule("0Harmony", "patching_framework", "Harmony", 0.95),
            new KnownComponentRule("Newtonsoft.Json", "serialization_library", "Newtonsoft.Json", 0.95),
            new KnownComponentRule("MessagePack", "serialization_library", "MessagePack", 0.9),
            new KnownComponentRule("protobuf-net", "serialization_library", "protobuf-net", 0.9),
            new KnownComponentRule("Google.Protobuf", "serialization_library", "Google.Protobuf", 0.9),
            new KnownComponentRule("MonoGame", "game_framework", "MonoGame", 0.9),
            new KnownComponentRule("Microsoft.Xna.Framework", "game_framework", "XNA / MonoGame", 0.92),
            new KnownComponentRule("Mono.Cecil", "analysis_library", "Mono.Cecil", 0.9),
            new KnownComponentRule("System.Windows", "ui_framework", "WPF", 0.85),
            new KnownComponentRule("PresentationFramework", "ui_framework", "WPF", 0.9),
            new KnownComponentRule("PresentationCore", "ui_framework", "WPF", 0.9),
            new KnownComponentRule("WindowsBase", "ui_framework", "WPF", 0.9),
            new KnownComponentRule("Avalonia", "ui_framework", "Avalonia", 0.9),
            new KnownComponentRule("Steamworks", "platform_sdk", "Steamworks", 0.88),
            new KnownComponentRule("Facepunch.Steamworks", "platform_sdk", "Steamworks", 0.88),
            new KnownComponentRule("Serilog", "logging_library", "Serilog", 0.88),
            new KnownComponentRule("NLog", "logging_library", "NLog", 0.88),
            new KnownComponentRule("log4net", "logging_library", "log4net", 0.88),
            new KnownComponentRule("Costura", "packaging_tool", "Costura.Fody", 0.88),
            new KnownComponentRule("Fody", "weaver", "Fody", 0.86)
        };

        static IReadOnlyList<KnownBinaryRule> GetKnownBinaryRules() => new[] {
            new KnownBinaryRule("mscorlib", null, "dotnet_framework", ".NET Framework Base Class Library", 0.99, true),
            new KnownBinaryRule("System", "b77a5c561934e089", "dotnet_framework", ".NET Framework assembly", 0.97, true),
            new KnownBinaryRule("System.Core", "b77a5c561934e089", "dotnet_framework", ".NET Framework assembly", 0.98, true),
            new KnownBinaryRule("PresentationFramework", "31bf3856ad364e35", "wpf_framework", "WPF", 0.98, true),
            new KnownBinaryRule("PresentationCore", "31bf3856ad364e35", "wpf_framework", "WPF", 0.98, true),
            new KnownBinaryRule("WindowsBase", "31bf3856ad364e35", "wpf_framework", "WPF", 0.98, true),
            new KnownBinaryRule("UnityEngine", null, "game_engine", "Unity", 0.96, true),
            new KnownBinaryRule("Unity.", null, "game_engine", "Unity", 0.9, true),
            new KnownBinaryRule("BepInEx", null, "modding_framework", "BepInEx", 0.95, true),
            new KnownBinaryRule("0Harmony", null, "patching_framework", "Harmony", 0.95, true),
            new KnownBinaryRule("HarmonyLib", null, "patching_framework", "Harmony", 0.95, true),
            new KnownBinaryRule("Newtonsoft.Json", null, "serialization_library", "Newtonsoft.Json", 0.95, true)
        };

        static bool MatchesKnownBinaryRule(AssemblyDef assembly, string publicKeyToken, KnownBinaryRule rule)
        {
            var name = assembly.Name.String;
            bool nameMatches = name.StartsWith(rule.NamePrefix, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(name, rule.NamePrefix, StringComparison.OrdinalIgnoreCase);
            if (!nameMatches)
                return false;
            if (string.IsNullOrWhiteSpace(rule.PublicKeyToken))
                return true;
            return string.Equals(publicKeyToken, rule.PublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        static string BuildKnownBinaryReason(AssemblyDef assembly, string publicKeyToken, KnownBinaryRule rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.PublicKeyToken))
                return $"Assembly name '{assembly.Name.String}' matched '{rule.NamePrefix}' and public key token '{publicKeyToken}'.";
            return $"Assembly name '{assembly.Name.String}' matched known prefix '{rule.NamePrefix}'.";
        }

        readonly struct KnownComponentRule
        {
            public KnownComponentRule(string prefix, string kind, string name, double confidence)
            {
                Prefix = prefix;
                Kind = kind;
                Name = name;
                Confidence = confidence;
            }

            public string Prefix { get; }
            public string Kind { get; }
            public string Name { get; }
            public double Confidence { get; }
        }

        readonly struct KnownBinaryRule
        {
            public KnownBinaryRule(string namePrefix, string? publicKeyToken, string kind, string identity, double confidence, bool namesTrusted)
            {
                NamePrefix = namePrefix;
                PublicKeyToken = publicKeyToken;
                Kind = kind;
                Identity = identity;
                Confidence = confidence;
                NamesTrusted = namesTrusted;
            }

            public string NamePrefix { get; }
            public string? PublicKeyToken { get; }
            public string Kind { get; }
            public string Identity { get; }
            public double Confidence { get; }
            public bool NamesTrusted { get; }
        }

        static IReadOnlyList<KnownRepositoryCandidate> GetKnownRepositoryCandidates() => new[] {
            new KnownRepositoryCandidate("HarmonyLib", "Harmony", "https://github.com/pardeike/Harmony", 0.95),
            new KnownRepositoryCandidate("0Harmony", "Harmony", "https://github.com/pardeike/Harmony", 0.95),
            new KnownRepositoryCandidate("BepInEx", "BepInEx", "https://github.com/BepInEx/BepInEx", 0.95),
            new KnownRepositoryCandidate("Newtonsoft.Json", "Newtonsoft.Json", "https://github.com/JamesNK/Newtonsoft.Json", 0.95),
            new KnownRepositoryCandidate("Mono.Cecil", "Mono.Cecil", "https://github.com/jbevain/cecil", 0.9),
            new KnownRepositoryCandidate("protobuf-net", "protobuf-net", "https://github.com/protobuf-net/protobuf-net", 0.9),
            new KnownRepositoryCandidate("Google.Protobuf", "Google.Protobuf", "https://github.com/protocolbuffers/protobuf", 0.85),
            new KnownRepositoryCandidate("MessagePack", "MessagePack", "https://github.com/MessagePack-CSharp/MessagePack-CSharp", 0.9),
            new KnownRepositoryCandidate("Avalonia", "Avalonia", "https://github.com/AvaloniaUI/Avalonia", 0.9),
            new KnownRepositoryCandidate("MonoGame", "MonoGame", "https://github.com/MonoGame/MonoGame", 0.9),
            new KnownRepositoryCandidate("TMPro", "TextMeshPro", "https://github.com/needle-mirror/com.unity.textmeshpro", 0.7),
            new KnownRepositoryCandidate("Fody", "Fody", "https://github.com/Fody/Fody", 0.85),
            new KnownRepositoryCandidate("Costura", "Costura.Fody", "https://github.com/Fody/Costura", 0.85)
        };

        readonly struct KnownRepositoryCandidate
        {
            public KnownRepositoryCandidate(string namePrefix, string displayName, string repository, double confidence)
            {
                NamePrefix = namePrefix;
                DisplayName = displayName;
                Repository = repository;
                Confidence = confidence;
            }

            public string NamePrefix { get; }
            public string DisplayName { get; }
            public string Repository { get; }
            public double Confidence { get; }
        }

        static int ComputeTriagePriority(Dictionary<string, object?> finding)
        {
            int priority = 0;
            var heuristic = finding.TryGetValue("heuristic", out var heuristicObj) ? heuristicObj?.ToString() ?? string.Empty : string.Empty;
            priority += heuristic switch
            {
                "anti_tamper" => 100,
                "anti_debug" => 95,
                "string_decryptor" => 85,
                "dynamic_code" => 80,
                "proxy_method" => 75,
                "delegate_creation" => 70,
                _ => 50
            };

            if (finding.TryGetValue("confidence", out var confidenceObj))
                priority += (int)(Convert.ToDouble(confidenceObj) * 10);
            if (finding.TryGetValue("caller_count", out var callerCountObj))
                priority += Math.Min(10, Convert.ToInt32(callerCountObj));

            return priority;
        }

        static bool IsDelegateType(TypeDef? type)
        {
            while (type != null)
            {
                if (string.Equals(type.FullName, "System.MulticastDelegate", StringComparison.Ordinal))
                    return true;
                type = type.BaseType?.ResolveTypeDef();
            }

            return false;
        }

        static Dictionary<string, object?> DescribeSymbolStatus(ModuleDef module)
        {
            var sidecarPdbPath = string.IsNullOrWhiteSpace(module.Location) ? null : Path.ChangeExtension(module.Location, ".pdb");
            var sidecarExists = !string.IsNullOrWhiteSpace(sidecarPdbPath) && File.Exists(sidecarPdbPath);
            return new Dictionary<string, object?> {
                ["module_name"] = module.Name.String,
                ["module_mvid"] = (module.Mvid ?? Guid.Empty).ToString("N"),
                ["file_path"] = module.Location,
                ["is_metadata_backed"] = module is ModuleDefMD,
                ["symbols_loaded"] = module.PdbState != null,
                ["has_sidecar_pdb"] = sidecarExists,
                ["sidecar_pdb_path"] = sidecarExists ? sidecarPdbPath : null
            };
        }

        static string? TryGetRepositoryUrl(AssemblyDef assembly)
        {
            foreach (var attribute in assembly.CustomAttributes) {
                var attributeType = attribute.AttributeType.FullName;
                if (string.Equals(attributeType, "System.Reflection.AssemblyMetadataAttribute", StringComparison.Ordinal) &&
                    attribute.ConstructorArguments.Count >= 2) {
                    var key = attribute.ConstructorArguments[0].Value?.ToString();
                    var value = attribute.ConstructorArguments[1].Value?.ToString();
                    if (string.Equals(key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "Repository", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "RepositoryBranch", StringComparison.OrdinalIgnoreCase))
                        return value;
                }

                if (string.Equals(attributeType, "System.Reflection.AssemblyCompanyAttribute", StringComparison.Ordinal) &&
                    attribute.ConstructorArguments.Count >= 1) {
                    var value = attribute.ConstructorArguments[0].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) && value!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }

            return null;
        }

        static string? TryGetInformationalVersion(AssemblyDef assembly)
        {
            foreach (var attribute in assembly.CustomAttributes) {
                if (!string.Equals(attribute.AttributeType.FullName, "System.Reflection.AssemblyInformationalVersionAttribute", StringComparison.Ordinal))
                    continue;
                if (attribute.ConstructorArguments.Count >= 1)
                    return attribute.ConstructorArguments[0].Value?.ToString();
            }

            return null;
        }

        static bool IsSimpleProxyInstruction(dnlib.DotNet.Emit.Code code) =>
            code == dnlib.DotNet.Emit.Code.Nop ||
            code == dnlib.DotNet.Emit.Code.Ret ||
            code == dnlib.DotNet.Emit.Code.Call ||
            code == dnlib.DotNet.Emit.Code.Callvirt ||
            code == dnlib.DotNet.Emit.Code.Newobj ||
            code == dnlib.DotNet.Emit.Code.Ldarg ||
            code == dnlib.DotNet.Emit.Code.Ldarg_0 ||
            code == dnlib.DotNet.Emit.Code.Ldarg_1 ||
            code == dnlib.DotNet.Emit.Code.Ldarg_2 ||
            code == dnlib.DotNet.Emit.Code.Ldarg_3 ||
            code == dnlib.DotNet.Emit.Code.Ldarg_S ||
            code == dnlib.DotNet.Emit.Code.Ldarga ||
            code == dnlib.DotNet.Emit.Code.Ldarga_S ||
            code == dnlib.DotNet.Emit.Code.Ldnull ||
            code == dnlib.DotNet.Emit.Code.Box ||
            code == dnlib.DotNet.Emit.Code.Castclass ||
            code == dnlib.DotNet.Emit.Code.Unbox_Any;

        List<Dictionary<string, object?>> BuildMethodRenameSuggestions(MethodDef method)
        {
            var evidence = CollectMethodSemanticEvidence(method);
            var roles = ScoreSemanticRoles(evidence);

            return roles
                .OrderByDescending(a => a.Score)
                .Take(5)
                .Select(a => new Dictionary<string, object?> {
                    ["suggested_name"] = BuildMethodSuggestionName(a.Role, method),
                    ["role"] = a.Role,
                    ["confidence"] = ComputeConfidence(a.Score),
                    ["evidence"] = a.Evidence.Distinct(StringComparer.Ordinal).Take(8).ToList()
                })
                .ToList();
        }

        List<Dictionary<string, object?>> BuildTypeRenameSuggestions(TypeDef type)
        {
            var roleEvidence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in type.Methods.Where(a => !a.IsConstructor && !a.IsStaticConstructor))
            {
                foreach (var suggestion in BuildMethodRenameSuggestions(method))
                {
                    var role = suggestion["role"]?.ToString() ?? "unknown";
                    if (!roleEvidence.TryGetValue(role, out var evidence))
                    {
                        evidence = new List<string>();
                        roleEvidence[role] = evidence;
                    }

                    if (suggestion.TryGetValue("evidence", out var evidenceObj) && evidenceObj is IEnumerable<string> strings)
                        evidence.AddRange(strings);
                }
            }

            return roleEvidence
                .Select(a => new {
                    Role = a.Key,
                    Score = a.Value.Count,
                    Evidence = a.Value
                })
                .OrderByDescending(a => a.Score)
                .Take(5)
                .Select(a => new Dictionary<string, object?> {
                    ["suggested_name"] = BuildTypeSuggestionName(a.Role, type),
                    ["role"] = a.Role,
                    ["confidence"] = ComputeConfidence(a.Score),
                    ["evidence"] = a.Evidence.Distinct(StringComparer.Ordinal).Take(8).ToList()
                })
                .ToList();
        }

        sealed class MethodSemanticEvidence
        {
            public List<string> CalledApis { get; } = new List<string>();
            public List<string> StringLiterals { get; } = new List<string>();
        }

        MethodSemanticEvidence CollectMethodSemanticEvidence(MethodDef method)
        {
            var evidence = new MethodSemanticEvidence();

            if (method.Body?.Instructions == null)
                return evidence;

            foreach (var instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Call || instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt) &&
                    instruction.Operand is IMethod calledMethod)
                    evidence.CalledApis.Add(calledMethod.FullName ?? string.Empty);

                if (instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Ldstr && instruction.Operand is string value && !string.IsNullOrWhiteSpace(value))
                    evidence.StringLiterals.Add(value);
            }

            return evidence;
        }

        IEnumerable<(string Role, int Score, List<string> Evidence)> ScoreSemanticRoles(MethodSemanticEvidence evidence)
        {
            var scoreMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var evidenceMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void Add(string role, int score, string reason)
            {
                if (!scoreMap.ContainsKey(role))
                    scoreMap[role] = 0;
                scoreMap[role] += score;

                if (!evidenceMap.TryGetValue(role, out var reasons))
                {
                    reasons = new List<string>();
                    evidenceMap[role] = reasons;
                }

                reasons.Add(reason);
            }

            foreach (var api in evidence.CalledApis)
            {
                var lower = api.ToLowerInvariant();
                if (lower.Contains("binaryreader") || lower.Contains("stream::read(") || lower.Contains("reader::"))
                    Add("reader", 3, api);
                if (lower.Contains("binarywriter") || lower.Contains("stream::write(") || lower.Contains("writer::"))
                    Add("writer", 3, api);
                if (lower.Contains("serialize") || lower.Contains("deserialize"))
                    Add("serializer", 4, api);
                if (lower.Contains("render") || lower.Contains("draw") || lower.Contains("spritebatch") || lower.Contains("texture2d"))
                    Add("renderer", 4, api);
                if (lower.Contains("zip") || lower.Contains("deflate") || lower.Contains("compress") || lower.Contains("archive"))
                    Add("archive", 4, api);
                if (lower.Contains("parse") || lower.Contains("token") || lower.Contains("lexer"))
                    Add("parser", 4, api);
                if (lower.Contains("json") || lower.Contains("xml"))
                    Add("format", 2, api);
                if (lower.Contains("createinstance") || lower.Contains("factory") || lower.Contains("newobj"))
                    Add("factory", 2, api);
                if (lower.Contains("cache"))
                    Add("cache", 2, api);
            }

            foreach (var text in evidence.StringLiterals)
            {
                var lower = text.ToLowerInvariant();
                if (lower.EndsWith(".png") || lower.EndsWith(".dds") || lower.EndsWith(".tga") || lower.Contains("texture"))
                    Add("renderer", 2, text);
                if (lower.EndsWith(".json") || lower.EndsWith(".xml") || lower.EndsWith(".ini"))
                    Add("format", 2, text);
                if (lower.EndsWith(".zip") || lower.EndsWith(".pak") || lower.EndsWith(".arc"))
                    Add("archive", 3, text);
                if (lower.Contains("read") || lower.Contains("load"))
                    Add("reader", 1, text);
                if (lower.Contains("write") || lower.Contains("save"))
                    Add("writer", 1, text);
                if (lower.Contains("mesh") || lower.Contains("sprite") || lower.Contains("shader") || lower.Contains("render"))
                    Add("renderer", 2, text);
            }

            return scoreMap.Select(a => (a.Key, a.Value, evidenceMap.TryGetValue(a.Key, out var reasons) ? reasons : new List<string>()));
        }

        static string BuildMethodSuggestionName(string role, MethodDef method)
        {
            var currentName = method.Name.String;
            return role switch
            {
                "reader" => "ReadData",
                "writer" => "WriteData",
                "serializer" => "SerializeData",
                "renderer" => "RenderAsset",
                "archive" => "ProcessArchive",
                "parser" => "ParseData",
                "factory" => "CreateObject",
                "cache" => "GetCachedValue",
                "format" => "HandleFormatData",
                _ => string.IsNullOrWhiteSpace(currentName) ? "RecoveredMethod" : $"Recovered{CapitalizeIdentifier(currentName)}"
            };
        }

        static string BuildTypeSuggestionName(string role, TypeDef type)
        {
            var currentName = type.Name.String;
            return role switch
            {
                "reader" => "DataReader",
                "writer" => "DataWriter",
                "serializer" => "DataSerializer",
                "renderer" => "AssetRenderer",
                "archive" => "ArchiveHandler",
                "parser" => "DataParser",
                "factory" => "ObjectFactory",
                "cache" => "ValueCache",
                "format" => "FormatHandler",
                _ => string.IsNullOrWhiteSpace(currentName) ? "RecoveredType" : $"Recovered{CapitalizeIdentifier(currentName)}"
            };
        }

        static double ComputeConfidence(int score)
        {
            if (score >= 8)
                return 0.9;
            if (score >= 5)
                return 0.75;
            if (score >= 3)
                return 0.6;
            return 0.4;
        }

        static string CapitalizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Symbol";

            var cleaned = new string(value.Where(char.IsLetterOrDigit).ToArray());
            if (cleaned.Length == 0)
                return "Symbol";

            return char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
        }

        TypeDef ResolveTypeReference(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Type reference arguments are required.");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            return resolved as TypeDef ?? throw new ArgumentException("Resolved reference is not a type. Provide type_full_name or a type member_id.");
        }

        MethodDef ResolveMethodReference(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Method reference arguments are required.");

            object resolved;
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                resolved = ResolveMemberIdCore(memberIdObj!.ToString()!);
            else
                resolved = ResolveReference(arguments);

            return resolved as MethodDef ?? throw new ArgumentException("Resolved reference is not a method. Provide method_name or a method member_id.");
        }

        IEnumerable<TypeDef> EnumerateAllLoadedTypes() =>
            ResolveAssembliesForSearch(null).SelectMany(a => a.Modules.SelectMany(b => GetAllTypesRecursive(b.Types)));

        static IEnumerable<MethodDef> EnumerateAllMethods(AssemblyDef assembly) =>
            assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)).SelectMany(a => a.Methods);

        static bool InheritsFrom(TypeDef candidate, TypeDef target)
        {
            var current = candidate.BaseType?.ResolveTypeDef();
            while (current != null)
            {
                if (string.Equals(current.FullName, target.FullName, StringComparison.Ordinal))
                    return true;
                current = current.BaseType?.ResolveTypeDef();
            }

            return false;
        }

        IEnumerable<TypeDef> FindDerivedTypesCore(TypeDef target) =>
            EnumerateAllLoadedTypes().Where(a => InheritsFrom(a, target));

        IEnumerable<MethodDef> FindCallersForMethod(MethodDef targetMethod)
        {
            var targetFullName = targetMethod.FullName;
            return EnumerateAllLoadedTypes()
                .SelectMany(a => a.Methods)
                .Where(a => a.Body?.Instructions != null)
                .Where(a => a.Body!.Instructions.Any(b =>
                    (b.OpCode.Code == dnlib.DotNet.Emit.Code.Call || b.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt) &&
                    b.Operand is MethodDef calledDef &&
                    string.Equals(calledDef.FullName, targetFullName, StringComparison.Ordinal)))
                .Distinct();
        }

        static IEnumerable<MethodDef> FindCallersForMethodStatic(MethodDef targetMethod)
        {
            var targetFullName = targetMethod.FullName;
            var assembly = targetMethod.Module.Assembly;
            if (assembly == null)
                return Enumerable.Empty<MethodDef>();

            return EnumerateAllMethods(assembly)
                .Where(a => a.Body?.Instructions != null)
                .Where(a => a.Body!.Instructions.Any(b =>
                    (b.OpCode.Code == dnlib.DotNet.Emit.Code.Call || b.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt) &&
                    b.Operand is MethodDef calledDef &&
                    string.Equals(calledDef.FullName, targetFullName, StringComparison.Ordinal)))
                .Distinct();
        }

        static IEnumerable<IMethod> EnumerateCalledMethods(MethodDef method)
        {
            if (method.Body?.Instructions == null)
                yield break;

            foreach (var instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Call || instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt) &&
                    instruction.Operand is IMethod calledMethod)
                    yield return calledMethod;
            }
        }

        IEnumerable<Dictionary<string, object?>> DescribeTypeUsages(TypeDef candidate, TypeDef targetType)
        {
            if (candidate.BaseType?.FullName == targetType.FullName)
            {
                yield return new Dictionary<string, object?> {
                    ["usage_kind"] = "base_type",
                    ["usage_location"] = candidate.FullName,
                    ["member_id"] = $"{(candidate.Module.Mvid ?? Guid.Empty):N}:{candidate.MDToken.Raw:X8}:T"
                };
            }

            foreach (var iface in candidate.Interfaces.Where(a => string.Equals(a.Interface?.FullName, targetType.FullName, StringComparison.Ordinal)))
            {
                _ = iface;
                yield return new Dictionary<string, object?> {
                    ["usage_kind"] = "interface",
                    ["usage_location"] = candidate.FullName,
                    ["member_id"] = $"{(candidate.Module.Mvid ?? Guid.Empty):N}:{candidate.MDToken.Raw:X8}:T"
                };
            }

            foreach (var field in candidate.Fields.Where(a => string.Equals(a.FieldType.FullName, targetType.FullName, StringComparison.Ordinal)))
            {
                yield return new Dictionary<string, object?> {
                    ["usage_kind"] = "field_type",
                    ["usage_location"] = $"{candidate.FullName}.{field.Name.String}",
                    ["member_id"] = $"{(field.Module.Mvid ?? Guid.Empty):N}:{field.MDToken.Raw:X8}:F"
                };
            }

            foreach (var method in candidate.Methods)
            {
                if (string.Equals(method.ReturnType.FullName, targetType.FullName, StringComparison.Ordinal))
                {
                    yield return new Dictionary<string, object?> {
                        ["usage_kind"] = "return_type",
                        ["usage_location"] = method.FullName,
                        ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M"
                    };
                }

                foreach (var parameter in method.Parameters.Where(a => string.Equals(a.Type.FullName, targetType.FullName, StringComparison.Ordinal)))
                {
                    yield return new Dictionary<string, object?> {
                        ["usage_kind"] = "parameter_type",
                        ["usage_location"] = $"{method.FullName}::{parameter.Name}",
                        ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M"
                    };
                }
            }
        }

        IEnumerable<Dictionary<string, object?>> EnumerateAttributeHits(AssemblyDef assembly, System.Text.RegularExpressions.Regex regex)
        {
            foreach (var attribute in assembly.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
            {
                yield return new Dictionary<string, object?> {
                    ["assembly_name"] = assembly.Name.String,
                    ["owner_kind"] = "assembly",
                    ["owner_full_name"] = assembly.FullName,
                    ["attribute_type"] = attribute.AttributeType.FullName
                };
            }

            foreach (var type in assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types)))
            {
                foreach (var attribute in type.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
                {
                    yield return new Dictionary<string, object?> {
                        ["assembly_name"] = assembly.Name.String,
                        ["owner_kind"] = "type",
                        ["owner_full_name"] = type.FullName,
                        ["attribute_type"] = attribute.AttributeType.FullName,
                        ["member_id"] = $"{(type.Module.Mvid ?? Guid.Empty):N}:{type.MDToken.Raw:X8}:T"
                    };
                }

                foreach (var method in type.Methods)
                {
                    foreach (var attribute in method.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
                    {
                        yield return new Dictionary<string, object?> {
                            ["assembly_name"] = assembly.Name.String,
                            ["owner_kind"] = "method",
                            ["owner_full_name"] = method.FullName,
                            ["attribute_type"] = attribute.AttributeType.FullName,
                            ["member_id"] = $"{(method.Module.Mvid ?? Guid.Empty):N}:{method.MDToken.Raw:X8}:M"
                        };
                    }
                }

                foreach (var field in type.Fields)
                {
                    foreach (var attribute in field.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
                    {
                        yield return new Dictionary<string, object?> {
                            ["assembly_name"] = assembly.Name.String,
                            ["owner_kind"] = "field",
                            ["owner_full_name"] = field.FullName,
                            ["attribute_type"] = attribute.AttributeType.FullName,
                            ["member_id"] = $"{(field.Module.Mvid ?? Guid.Empty):N}:{field.MDToken.Raw:X8}:F"
                        };
                    }
                }

                foreach (var property in type.Properties)
                {
                    foreach (var attribute in property.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
                    {
                        yield return new Dictionary<string, object?> {
                            ["assembly_name"] = assembly.Name.String,
                            ["owner_kind"] = "property",
                            ["owner_full_name"] = property.FullName,
                            ["attribute_type"] = attribute.AttributeType.FullName,
                            ["member_id"] = $"{(property.Module.Mvid ?? Guid.Empty):N}:{property.MDToken.Raw:X8}:P"
                        };
                    }
                }

                foreach (var @event in type.Events)
                {
                    foreach (var attribute in @event.CustomAttributes.Where(a => regex.IsMatch(a.AttributeType.FullName)))
                    {
                        yield return new Dictionary<string, object?> {
                            ["assembly_name"] = assembly.Name.String,
                            ["owner_kind"] = "event",
                            ["owner_full_name"] = @event.FullName,
                            ["attribute_type"] = attribute.AttributeType.FullName,
                            ["member_id"] = $"{(@event.Module.Mvid ?? Guid.Empty):N}:{@event.MDToken.Raw:X8}:E"
                        };
                    }
                }
            }
        }

        Dictionary<string, object?> DescribeMethodReference(MethodDef method) =>
            DescribeReferenceWithMemberId(method);

        Dictionary<string, object?> DescribeMethodLikeReference(IMethod method)
        {
            if (method is MethodDef methodDef)
                return DescribeReferenceWithMemberId(methodDef);

            return new Dictionary<string, object?> {
                ["reference_kind"] = "MethodRef",
                ["name"] = method.Name,
                ["full_name"] = method.FullName,
                ["declaring_type"] = method.DeclaringType?.FullName,
                ["signature"] = method.MethodSig?.ToString()
            };
        }

        sealed class DictionaryByJsonComparer : IEqualityComparer<Dictionary<string, object?>>
        {
            public static readonly DictionaryByJsonComparer Instance = new DictionaryByJsonComparer();

            public bool Equals(Dictionary<string, object?>? x, Dictionary<string, object?>? y) =>
                string.Equals(Serialize(x), Serialize(y), StringComparison.Ordinal);

            public int GetHashCode(Dictionary<string, object?> obj) =>
                StringComparer.Ordinal.GetHashCode(Serialize(obj));

            static string Serialize(Dictionary<string, object?>? value) =>
                JsonSerializer.Serialize(value ?? new Dictionary<string, object?>());
        }

        static Dictionary<string, object?> BuildMemberPayload(ModuleDef module, uint rawToken, char kind, Dictionary<string, object?> payload)
        {
            payload["reference_kind"] = kind.ToString();
            payload["token"] = $"0x{rawToken:X8}";
            payload["module_mvid"] = (module.Mvid ?? Guid.Empty).ToString("N");
            payload["member_id"] = $"{(module.Mvid ?? Guid.Empty):N}:{rawToken:X8}:{kind}";
            return payload;
        }

        static Dictionary<string, object?> DescribeReferenceWithMemberIdStatic(MethodDef method) =>
            BuildMemberPayload(method.Module, method.MDToken.Raw, 'M', new Dictionary<string, object?> {
                ["assembly_name"] = method.Module.Assembly?.Name.String,
                ["module_name"] = method.Module.Name,
                ["name"] = method.Name.String,
                ["full_name"] = method.FullName,
                ["declaring_type"] = method.DeclaringType?.FullName,
                ["signature"] = method.MethodSig?.ToString()
            });

        string ResolvePortableExecutablePath(Dictionary<string, object> arguments)
        {
            if (arguments.TryGetValue("file_path", out var filePathObj) && !string.IsNullOrWhiteSpace(filePathObj?.ToString()))
            {
                var explicitPath = filePathObj!.ToString()!;
                if (!File.Exists(explicitPath))
                    throw new ArgumentException($"File not found: {explicitPath}");
                return explicitPath;
            }

            var assembly = ResolveAssembly(arguments);
            var module = assembly.ManifestModule ?? assembly.Modules.FirstOrDefault();
            var modulePath = module?.Location;
            if (string.IsNullOrWhiteSpace(modulePath) || !File.Exists(modulePath))
                throw new ArgumentException($"The loaded assembly '{assembly.Name.String}' does not have a readable on-disk PE path. Provide file_path explicitly.");

            return modulePath;
        }

        static Dictionary<string, object?> ExtractNativeExportsFromPe(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peHeaderOffset = br.ReadInt32();
            fs.Seek(peHeaderOffset, SeekOrigin.Begin);

            if (br.ReadUInt32() != 0x00004550)
                throw new ArgumentException($"File '{filePath}' is not a PE image.");

            ushort machine = br.ReadUInt16();
            ushort numberOfSections = br.ReadUInt16();
            br.BaseStream.Seek(12, SeekOrigin.Current);
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            ushort characteristics = br.ReadUInt16();

            long optionalHeaderStart = br.BaseStream.Position;
            ushort magic = br.ReadUInt16();
            bool isPe32Plus = magic == 0x20b;

            fs.Seek(peHeaderOffset + 4 + 20 + sizeOfOptionalHeader, SeekOrigin.Begin);
            var sections = new List<(string Name, uint VirtualAddress, uint VirtualSize, uint PointerToRawData, uint SizeOfRawData)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                var sectionNameBytes = br.ReadBytes(8);
                var sectionName = Encoding.ASCII.GetString(sectionNameBytes).TrimEnd('\0');
                uint virtualSize = br.ReadUInt32();
                uint virtualAddress = br.ReadUInt32();
                uint sizeOfRawData = br.ReadUInt32();
                uint pointerToRawData = br.ReadUInt32();
                br.BaseStream.Seek(16, SeekOrigin.Current);
                sections.Add((sectionName, virtualAddress, virtualSize, pointerToRawData, sizeOfRawData));
            }

            fs.Seek(optionalHeaderStart, SeekOrigin.Begin);
            br.ReadUInt16();
            br.BaseStream.Seek(isPe32Plus ? 222 : 206, SeekOrigin.Current);
            uint exportRva = br.ReadUInt32();
            uint exportSize = br.ReadUInt32();

            if (exportRva == 0 || exportSize == 0)
            {
                return new Dictionary<string, object?> {
                    ["file_path"] = filePath,
                    ["machine"] = machine.ToString("X4"),
                    ["characteristics"] = $"0x{characteristics:X4}",
                    ["items"] = new List<object>(),
                    ["total_count"] = 0,
                    ["has_exports"] = false
                };
            }

            long RvaToOffset(uint rva)
            {
                foreach (var section in sections)
                {
                    var length = Math.Max(section.VirtualSize, section.SizeOfRawData);
                    if (rva >= section.VirtualAddress && rva < section.VirtualAddress + length)
                        return section.PointerToRawData + (rva - section.VirtualAddress);
                }

                throw new ArgumentException($"RVA 0x{rva:X8} is not contained in any section.");
            }

            fs.Seek(RvaToOffset(exportRva), SeekOrigin.Begin);
            br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt16();
            br.ReadUInt16();
            uint nameRva = br.ReadUInt32();
            uint ordinalBase = br.ReadUInt32();
            uint numberOfFunctions = br.ReadUInt32();
            uint numberOfNames = br.ReadUInt32();
            uint addressOfFunctions = br.ReadUInt32();
            uint addressOfNames = br.ReadUInt32();
            uint addressOfOrdinals = br.ReadUInt32();

            string moduleName = string.Empty;
            if (nameRva != 0)
            {
                fs.Seek(RvaToOffset(nameRva), SeekOrigin.Begin);
                moduleName = ReadNullTerminatedAscii(fs);
            }

            var items = new List<Dictionary<string, object?>>();
            for (uint i = 0; i < numberOfNames; i++)
            {
                fs.Seek(RvaToOffset(addressOfNames + (i * 4)), SeekOrigin.Begin);
                uint exportNameRva = br.ReadUInt32();
                fs.Seek(RvaToOffset(exportNameRva), SeekOrigin.Begin);
                string exportName = ReadNullTerminatedAscii(fs);

                fs.Seek(RvaToOffset(addressOfOrdinals + (i * 2)), SeekOrigin.Begin);
                ushort ordinalIndex = br.ReadUInt16();

                fs.Seek(RvaToOffset(addressOfFunctions + (ordinalIndex * 4u)), SeekOrigin.Begin);
                uint functionRva = br.ReadUInt32();

                items.Add(new Dictionary<string, object?> {
                    ["name"] = exportName,
                    ["ordinal"] = ordinalBase + ordinalIndex,
                    ["rva"] = $"0x{functionRva:X8}",
                    ["is_forwarder"] = functionRva >= exportRva && functionRva < exportRva + exportSize
                });
            }

            return new Dictionary<string, object?> {
                ["file_path"] = filePath,
                ["module_name"] = string.IsNullOrWhiteSpace(moduleName) ? Path.GetFileName(filePath) : moduleName,
                ["machine"] = machine.ToString("X4"),
                ["characteristics"] = $"0x{characteristics:X4}",
                ["function_count"] = numberOfFunctions,
                ["named_export_count"] = numberOfNames,
                ["items"] = items,
                ["total_count"] = items.Count,
                ["has_exports"] = items.Count > 0
            };
        }

        static string ReadNullTerminatedAscii(Stream stream)
        {
            var builder = new StringBuilder();
            int value;
            while ((value = stream.ReadByte()) > 0)
                builder.Append((char)value);
            return builder.ToString();
        }

        /// <summary>
        /// Finds an assembly by its loaded file path. Returns null if not found.
        /// Normalizes separators so forward- and back-slashes are treated the same.
        /// Must NOT be called on the UI thread — it will marshal internally.
        /// </summary>
        AssemblyDef? FindAssemblyByFilePath(string filePath)
        {
            var normalized = filePath.Replace('/', '\\');
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Where(m => m.Document?.AssemblyDef != null &&
                                !string.IsNullOrEmpty(m.Document.Filename) &&
                                (m.Document.Filename.Replace('/', '\\'))
                                    .Equals(normalized, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Document!.AssemblyDef)
                    .FirstOrDefault());
        }

        static TypeDef? FindTypeInAssembly(AssemblyDef assembly, string fullName) =>
            assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types))
                .FirstOrDefault(a => string.Equals(a.FullName, fullName, StringComparison.Ordinal));

        string EncodeCursor(int offset, int pageSize)
        {
            var cursorData = new { offset, pageSize };
            var json = JsonSerializer.Serialize(cursorData);
            var bytes = Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes);
        }

        static System.Text.RegularExpressions.Regex BuildPatternRegex(string pattern)
        {
            bool isRegex = pattern.IndexOfAny(new[] { '^', '$', '[', '(', '|', '+', '{' }) >= 0;
            if (isRegex)
                return new System.Text.RegularExpressions.Regex(pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            var escaped = System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace(@"\*", ".*").Replace(@"\?", ".");
            return new System.Text.RegularExpressions.Regex("^" + escaped + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        (int offset, int pageSize) DecodeCursor(string? cursor)
        {
            const int defaultPageSize = 50;
            if (string.IsNullOrEmpty(cursor))
                return (0, defaultPageSize);

            try
            {
                var bytes = Convert.FromBase64String(cursor);
                var json = Encoding.UTF8.GetString(bytes);
                var cursorData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (cursorData == null)
                    throw new ArgumentException("Invalid cursor: cursor data is null");

                if (!cursorData.TryGetValue("offset", out var offsetObj) || !(offsetObj is JsonElement offsetElem) || !offsetElem.TryGetInt32(out var offset))
                    throw new ArgumentException("Invalid cursor: missing or invalid 'offset' field");

                if (!cursorData.TryGetValue("pageSize", out var pageSizeObj) || !(pageSizeObj is JsonElement pageSizeElem) || !pageSizeElem.TryGetInt32(out var pageSize))
                    throw new ArgumentException("Invalid cursor: missing or invalid 'pageSize' field");

                if (offset < 0)
                    throw new ArgumentException("Invalid cursor: offset cannot be negative");

                if (pageSize <= 0)
                    throw new ArgumentException("Invalid cursor: pageSize must be positive");

                return (offset, pageSize);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid cursor: {ex.Message}");
            }
        }

        CallToolResult CreatePaginatedResponse<T>(List<T> allItems, int offset, int pageSize)
        {
            var itemsToReturn = allItems.Skip(offset).Take(pageSize).ToList();
            var hasMore = offset + pageSize < allItems.Count;

            var response = new Dictionary<string, object>
            {
                ["items"] = itemsToReturn,
                ["total_count"] = allItems.Count,
                ["returned_count"] = itemsToReturn.Count
            };

            if (hasMore)
            {
                response["nextCursor"] = EncodeCursor(offset + pageSize, pageSize);
            }

            var result = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> {
                    new ToolContent { Text = result }
                }
            };
        }

        /// <summary>
        /// Selects an assembly in the document tree view and opens it in a tab,
        /// making it the "current" assembly for all subsequent MCP operations.
        /// </summary>
        public CallToolResult SelectAssembly(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("assembly_name", out var nameObj))
                throw new ArgumentException("assembly_name is required");

            var assemblyName = nameObj.ToString() ?? string.Empty;

            // Optional file_path for disambiguation when multiple assemblies share the same name
            AssemblyDef? assembly = null;
            if (arguments.TryGetValue("file_path", out var fpObj) && fpObj?.ToString() is string fp && !string.IsNullOrWhiteSpace(fp))
            {
                assembly = FindAssemblyByFilePath(fp);
                if (assembly == null)
                    throw new ArgumentException($"No assembly loaded from path: {fp}. Use list_assemblies to see FilePath values.");
            }
            else
            {
                assembly = FindAssemblyByName(assemblyName);
                if (assembly == null)
                    throw new ArgumentException($"Assembly not found: {assemblyName}. Use list_assemblies to see loaded assemblies.");
            }

            // All tree-view operations must be on the UI thread
            var info = System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Find the tree node for this assembly
                var assemblyNode = documentTreeView.FindNode(assembly);
                if (assemblyNode == null)
                    return new { Selected = false, OpenedTab = false, Error = "Tree node not found for assembly" };

                // Select it in the tree view (highlights it in the left panel)
                documentTreeView.TreeView.SelectItems(new[] { assemblyNode });
                documentTreeView.TreeView.ScrollIntoView();

                // Open/navigate to it in the active tab so decompiler shows it
                bool openedTab = false;
                try
                {
                    documentTabService.Value.FollowReference(assembly, newTab: false, setFocus: true);
                    openedTab = true;
                }
                catch { }

                return new { Selected = true, OpenedTab = openedTab, Error = (string)null! };
            });

            var response = new
            {
                Assembly = assembly.FullName,
                Selected = info.Selected,
                OpenedTab = info.OpenedTab,
                Error = info.Error
            };

            return new CallToolResult
            {
                Content = new List<ToolContent> {
                    new ToolContent { Text = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }) }
                }
            };
        }

        IEnumerable<(ModuleDef Module, EmbeddedResource Resource, ResourceElementSet Set)> EnumerateResourceElementSets(AssemblyDef assembly, string? resourceName = null)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var resource in module.Resources.OfType<EmbeddedResource>())
                {
                    if (!string.IsNullOrWhiteSpace(resourceName) &&
                        !string.Equals(resource.Name.String, resourceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ResourceElementSet? set;
                    try
                    {
                        set = dnlib.DotNet.Resources.ResourceReader.Read(module, resource.CreateReader());
                    }
                    catch
                    {
                        continue;
                    }

                    if (set == null)
                        continue;

                    yield return (module, resource, set);
                }
            }
        }

        IEnumerable<(ModuleDef Module, string ResourceName, ResourceElement Element, byte[] Data)> EnumerateBamlElements(AssemblyDef assembly, string? resourceName = null, string? elementName = null)
        {
            foreach (var entry in EnumerateResourceElementSets(assembly, resourceName))
            {
                foreach (var element in entry.Set.ResourceElements)
                {
                    if (!element.Name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(elementName) &&
                        !string.Equals(element.Name, elementName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!TryGetResourceElementBytes(element, out var bytes))
                        continue;

                    yield return (entry.Module, entry.Resource.Name.String, element, bytes);
                }
            }
        }

        static bool TryGetResourceElementBytes(ResourceElement element, out byte[] data)
        {
            data = Array.Empty<byte>();
            if (element.ResourceData is not BuiltInResourceData builtIn)
                return false;

            if (builtIn.Code != ResourceTypeCode.ByteArray && builtIn.Code != ResourceTypeCode.Stream)
                return false;

            data = builtIn.Data as byte[] ?? Array.Empty<byte>();
            return data.Length > 0;
        }

        Dictionary<string, object?> DescribeResourceElement(ModuleDef module, EmbeddedResource resource, ResourceElement element)
        {
            var payload = new Dictionary<string, object?> {
                ["assembly_name"] = module.Assembly?.Name.String,
                ["module_name"] = module.Name,
                ["resource_name"] = resource.Name.String,
                ["element_name"] = element.Name,
                ["resource_data_kind"] = DescribeResourceDataKind(element.ResourceData),
                ["classification"] = element.Name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase) ? "baml" :
                    element.Name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? "xaml" :
                    element.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image" :
                    element.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" :
                    "resource_element"
            };

            if (element.ResourceData is BuiltInResourceData builtIn)
            {
                payload["resource_type_code"] = builtIn.Code.ToString();
                switch (builtIn.Code)
                {
                    case ResourceTypeCode.String:
                        payload["value_preview"] = ((string)builtIn.Data).Length > 120
                            ? ((string)builtIn.Data).Substring(0, 120)
                            : (string)builtIn.Data;
                        break;
                    case ResourceTypeCode.ByteArray:
                    case ResourceTypeCode.Stream:
                        var bytes = builtIn.Data as byte[] ?? Array.Empty<byte>();
                        payload["size"] = bytes.Length;
                        payload["hex_preview"] = ToHexPreview(bytes, 48);
                        break;
                }
            }

            return payload;
        }

        static string DescribeResourceDataKind(IResourceData resourceData) =>
            resourceData switch
            {
                BuiltInResourceData builtIn => builtIn.Code.ToString(),
                BinaryResourceData binary => binary.TypeName,
                _ => resourceData.GetType().Name
            };

        string DecompileBamlBytes(ModuleDef module, byte[] data, out IList<string> assemblyReferences, out string? typeName)
        {
            if (bamlDecompiler == null || xamlOutputOptionsProvider == null)
                throw new InvalidOperationException("The dnSpy BAML decompiler extension is not available in this host.");

            var options = BamlDecompilerOptions.Create(decompilerService.Decompiler);
            using var stream = new MemoryStream();
            assemblyReferences = bamlDecompiler.Value.Decompile(module, data, System.Threading.CancellationToken.None, options, stream, xamlOutputOptionsProvider.Value.Default);
            typeName = bamlDecompiler.Value.DecompileTypeName(module, data, System.Threading.CancellationToken.None, options);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        static string BuildProjectTypeRelativePath(TypeDef type)
        {
            var segments = new List<string>();
            var namespaceName = type.Namespace.String;
            if (!string.IsNullOrWhiteSpace(namespaceName))
                segments.AddRange(namespaceName.Split('.').Where(a => !string.IsNullOrWhiteSpace(a)).Select(SanitizePathSegment));

            segments.Add($"{SanitizePathSegment(type.Name.String)}.cs");
            return Path.Combine(segments.ToArray());
        }

        static string BuildSdkProjectText(AssemblyDef assembly, bool includeResources)
        {
            var targetFramework = NormalizeTargetFramework(TryGetTargetFramework(assembly));
            var lines = new List<string> {
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                $"    <TargetFramework>{targetFramework}</TargetFramework>",
                "    <ImplicitUsings>disable</ImplicitUsings>",
                "    <Nullable>disable</Nullable>",
                "  </PropertyGroup>"
            };

            if (includeResources)
            {
                lines.Add("  <ItemGroup>");
                lines.Add("    <EmbeddedResource Include=\"resources\\**\\*\" />");
                lines.Add("    <Page Include=\"xaml\\**\\*.xaml\" />");
                lines.Add("  </ItemGroup>");
            }

            lines.Add("</Project>");
            return string.Join(Environment.NewLine, lines);
        }

        static string NormalizeTargetFramework(string? targetFramework)
        {
            if (string.IsNullOrWhiteSpace(targetFramework))
                return "net48";

            if (targetFramework.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase))
                return "net48";
            if (targetFramework.StartsWith(".NETCoreApp", StringComparison.OrdinalIgnoreCase))
                return "net8.0";
            if (targetFramework.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase))
                return "netstandard2.0";

            return targetFramework;
        }

        static string SanitizePathSegment(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(a => invalidChars.Contains(a) ? '_' : a).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "item" : cleaned;
        }

        static void WriteSimpleResxFile(ResourceElementSet set, string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var settings = new XmlWriterSettings {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };

            using var writer = XmlWriter.Create(filePath, settings);
            writer.WriteStartDocument();
            writer.WriteStartElement("root");

            WriteResxHeader(writer, "resmimetype", "text/microsoft-resx");
            WriteResxHeader(writer, "version", "2.0");
            WriteResxHeader(writer, "reader", "System.Resources.ResXResourceReader");
            WriteResxHeader(writer, "writer", "System.Resources.ResXResourceWriter");

            foreach (var element in set.ResourceElements.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartElement("data");
                writer.WriteAttributeString("name", element.Name);
                writer.WriteAttributeString("xml", "space", null, "preserve");

                var value = GetResourceElementResxValue(element, out var comment);
                writer.WriteElementString("value", value);
                if (!string.IsNullOrWhiteSpace(comment))
                    writer.WriteElementString("comment", comment);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        static void WriteResxHeader(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement("resheader");
            writer.WriteAttributeString("name", name);
            writer.WriteElementString("value", value);
            writer.WriteEndElement();
        }

        static string GetResourceElementResxValue(ResourceElement element, out string? comment)
        {
            comment = null;
            if (element.ResourceData is not BuiltInResourceData builtIn)
            {
                comment = $"Unsupported resource data kind: {element.ResourceData.GetType().Name}";
                return string.Empty;
            }

            switch (builtIn.Code)
            {
                case ResourceTypeCode.String:
                    return (string)builtIn.Data;
                case ResourceTypeCode.Boolean:
                case ResourceTypeCode.Char:
                case ResourceTypeCode.Byte:
                case ResourceTypeCode.SByte:
                case ResourceTypeCode.Int16:
                case ResourceTypeCode.UInt16:
                case ResourceTypeCode.Int32:
                case ResourceTypeCode.UInt32:
                case ResourceTypeCode.Int64:
                case ResourceTypeCode.UInt64:
                case ResourceTypeCode.Single:
                case ResourceTypeCode.Double:
                case ResourceTypeCode.Decimal:
                case ResourceTypeCode.DateTime:
                case ResourceTypeCode.TimeSpan:
                    return Convert.ToString(builtIn.Data, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                case ResourceTypeCode.ByteArray:
                case ResourceTypeCode.Stream:
                    comment = "Base64-encoded binary resource.";
                    return Convert.ToBase64String((byte[])builtIn.Data);
                default:
                    comment = $"Stored as string representation from {builtIn.Code}.";
                    return builtIn.Data?.ToString() ?? string.Empty;
            }
        }

        static int ReadIntValue(object value, int defaultValue)
        {
            if (value is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var number))
                    return number;
                if (elem.ValueKind == JsonValueKind.String && int.TryParse(elem.GetString(), out number))
                    return number;
                return defaultValue;
            }

            return int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
        }

        List<Dictionary<string, object?>> BuildPseudoSsa(MethodDef method)
        {
            var instructions = method.Body?.Instructions ?? throw new ArgumentException("Method body required");
            var locals = method.Body?.Variables.Select((a, index) => $"v{index}_0").ToArray() ?? Array.Empty<string>();
            var stack = new Stack<string>();
            var ssa = new List<Dictionary<string, object?>>();
            var localVersions = new int[locals.Length];
            int tempVersion = 0;

            foreach (var instruction in instructions)
            {
                var op = instruction.OpCode.Code;
                string? produced = null;
                string expression = instruction.ToString();

                switch (op)
                {
                    case Code.Ldc_I4:
                    case Code.Ldc_I4_S:
                    case Code.Ldc_I4_0:
                    case Code.Ldc_I4_1:
                    case Code.Ldc_I4_2:
                    case Code.Ldc_I4_3:
                    case Code.Ldc_I4_4:
                    case Code.Ldc_I4_5:
                    case Code.Ldc_I4_6:
                    case Code.Ldc_I4_7:
                    case Code.Ldc_I4_8:
                    case Code.Ldc_I4_M1:
                    case Code.Ldc_I8:
                    case Code.Ldc_R4:
                    case Code.Ldc_R8:
                    case Code.Ldstr:
                        produced = $"t{tempVersion++}";
                        expression = $"{produced} = {FormatOperandLiteral(instruction.Operand)}";
                        stack.Push(produced);
                        break;

                    case Code.Ldarg:
                    case Code.Ldarg_S:
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                        produced = $"t{tempVersion++}";
                        expression = $"{produced} = {DescribeArgumentReference(method, instruction)}";
                        stack.Push(produced);
                        break;

                    case Code.Ldloc:
                    case Code.Ldloc_S:
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                        var loadedLocal = ResolveLocalIndex(method, instruction);
                        produced = $"t{tempVersion++}";
                        expression = $"{produced} = {GetCurrentLocalSsaName(locals, localVersions, loadedLocal)}";
                        stack.Push(produced);
                        break;

                    case Code.Stloc:
                    case Code.Stloc_S:
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                        var storedLocal = ResolveLocalIndex(method, instruction);
                        var source = stack.Count > 0 ? stack.Pop() : "?";
                        localVersions[storedLocal]++;
                        expression = $"{GetCurrentLocalSsaName(locals, localVersions, storedLocal)} = {source}";
                        break;

                    case Code.Add:
                    case Code.Sub:
                    case Code.Mul:
                    case Code.Div:
                    case Code.Rem:
                    case Code.Xor:
                    case Code.And:
                    case Code.Or:
                        var right = stack.Count > 0 ? stack.Pop() : "?";
                        var left = stack.Count > 0 ? stack.Pop() : "?";
                        produced = $"t{tempVersion++}";
                        expression = $"{produced} = {left} {GetBinaryOperator(op)} {right}";
                        stack.Push(produced);
                        break;

                    case Code.Call:
                    case Code.Callvirt:
                        var calledMethod = instruction.Operand as IMethod;
                        var argCount = calledMethod?.MethodSig?.GetParamCount() ?? 0;
                        var callArgs = new List<string>();
                        for (int i = 0; i < argCount && stack.Count > 0; i++)
                            callArgs.Insert(0, stack.Pop());
                        produced = calledMethod?.MethodSig?.RetType.ElementType != ElementType.Void ? $"t{tempVersion++}" : null;
                        expression = produced != null
                            ? $"{produced} = call {calledMethod?.FullName}({string.Join(", ", callArgs)})"
                            : $"call {calledMethod?.FullName}({string.Join(", ", callArgs)})";
                        if (produced != null)
                            stack.Push(produced);
                        break;
                }

                ssa.Add(new Dictionary<string, object?> {
                    ["offset"] = $"0x{instruction.Offset:X4}",
                    ["opcode"] = instruction.OpCode.Code.ToString(),
                    ["expression"] = expression,
                    ["stack_after"] = stack.Reverse().ToList()
                });
            }

            return ssa;
        }

        List<Dictionary<string, object?>> EmulateMethodCore(MethodDef method, int maxSteps)
        {
            var instructions = method.Body?.Instructions ?? throw new ArgumentException("Method body required");
            var trace = new List<Dictionary<string, object?>>();
            var stack = new Stack<object?>();
            var locals = new object?[method.Body?.Variables.Count ?? 0];

            int step = 0;
            for (int ip = 0; ip < instructions.Count && step < maxSteps; ip++, step++)
            {
                var instruction = instructions[ip];
                string note = string.Empty;

                switch (instruction.OpCode.Code)
                {
                    case Code.Nop:
                        break;
                    case Code.Ldc_I4:
                    case Code.Ldc_I4_S:
                    case Code.Ldc_I4_0:
                    case Code.Ldc_I4_1:
                    case Code.Ldc_I4_2:
                    case Code.Ldc_I4_3:
                    case Code.Ldc_I4_4:
                    case Code.Ldc_I4_5:
                    case Code.Ldc_I4_6:
                    case Code.Ldc_I4_7:
                    case Code.Ldc_I4_8:
                    case Code.Ldc_I4_M1:
                        stack.Push(GetIntConstant(instruction));
                        break;
                    case Code.Ldstr:
                        stack.Push(instruction.Operand?.ToString());
                        break;
                    case Code.Ldloc:
                    case Code.Ldloc_S:
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                        stack.Push(locals[ResolveLocalIndex(method, instruction)]);
                        break;
                    case Code.Stloc:
                    case Code.Stloc_S:
                    case Code.Stloc_0:
                    case Code.Stloc_1:
                    case Code.Stloc_2:
                    case Code.Stloc_3:
                        locals[ResolveLocalIndex(method, instruction)] = stack.Count > 0 ? stack.Pop() : null;
                        break;
                    case Code.Add:
                    case Code.Sub:
                    case Code.Mul:
                    case Code.Div:
                    case Code.Rem:
                    case Code.Xor:
                    case Code.And:
                    case Code.Or:
                        if (!TryApplyBinaryOperation(instruction.OpCode.Code, stack, out note))
                            note = "Stopped on non-constant arithmetic.";
                        break;
                    case Code.Br:
                    case Code.Br_S:
                        ip = instructions.IndexOf((Instruction)instruction.Operand) - 1;
                        note = "Unconditional branch taken.";
                        break;
                    case Code.Brtrue:
                    case Code.Brtrue_S:
                    case Code.Brfalse:
                    case Code.Brfalse_S:
                        note = "Conditional branch encountered; execution trace stopped because the condition is unknown.";
                        trace.Add(BuildEmulationTraceRow(instruction, stack, locals, note));
                        return trace;
                    case Code.Call:
                    case Code.Callvirt:
                        note = $"Encountered call to {(instruction.Operand as IMethod)?.FullName}; external effects not executed.";
                        break;
                    case Code.Ret:
                        note = "Return reached.";
                        trace.Add(BuildEmulationTraceRow(instruction, stack, locals, note));
                        return trace;
                    default:
                        note = "Instruction not modeled; trace continues without executing side effects.";
                        break;
                }

                trace.Add(BuildEmulationTraceRow(instruction, stack, locals, note));
            }

            if (step >= maxSteps)
            {
                trace.Add(new Dictionary<string, object?> {
                    ["offset"] = null,
                    ["opcode"] = "limit",
                    ["note"] = $"Execution stopped after {maxSteps} steps."
                });
            }

            return trace;
        }

        static Dictionary<string, object?> BuildEmulationTraceRow(Instruction instruction, Stack<object?> stack, object?[] locals, string note) =>
            new Dictionary<string, object?> {
                ["offset"] = $"0x{instruction.Offset:X4}",
                ["opcode"] = instruction.OpCode.Code.ToString(),
                ["operand"] = instruction.Operand?.ToString(),
                ["stack"] = stack.Reverse().Select(FormatRuntimeValue).ToList(),
                ["locals"] = locals.Select((a, index) => new Dictionary<string, object?> {
                    ["index"] = index,
                    ["value"] = FormatRuntimeValue(a)
                }).ToList(),
                ["note"] = note
            };

        static string FormatRuntimeValue(object? value) =>
            value switch
            {
                null => "null",
                string text => $"\"{text}\"",
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? value.ToString() ?? "<?>"
            };

        static bool TryApplyBinaryOperation(Code code, Stack<object?> stack, out string note)
        {
            note = string.Empty;
            if (stack.Count < 2)
            {
                note = "Insufficient stack values.";
                return false;
            }

            var right = stack.Pop();
            var left = stack.Pop();
            if (!TryConvertToLong(left, out var leftValue) || !TryConvertToLong(right, out var rightValue))
            {
                stack.Push(left);
                stack.Push(right);
                return false;
            }

            long result = code switch
            {
                Code.Add => leftValue + rightValue,
                Code.Sub => leftValue - rightValue,
                Code.Mul => leftValue * rightValue,
                Code.Div => rightValue == 0 ? 0 : leftValue / rightValue,
                Code.Rem => rightValue == 0 ? 0 : leftValue % rightValue,
                Code.Xor => leftValue ^ rightValue,
                Code.And => leftValue & rightValue,
                Code.Or => leftValue | rightValue,
                _ => 0
            };

            stack.Push(result);
            note = $"Constant-folded {code}.";
            return true;
        }

        static bool TryConvertToLong(object? value, out long result)
        {
            switch (value)
            {
                case sbyte a: result = a; return true;
                case byte a: result = a; return true;
                case short a: result = a; return true;
                case ushort a: result = a; return true;
                case int a: result = a; return true;
                case uint a: result = a; return true;
                case long a: result = a; return true;
                case ulong a when a <= long.MaxValue: result = (long)a; return true;
                default:
                    result = 0;
                    return false;
            }
        }

        static string DescribeArgumentReference(MethodDef method, Instruction instruction)
        {
            var index = instruction.OpCode.Code switch
            {
                Code.Ldarg_0 => 0,
                Code.Ldarg_1 => 1,
                Code.Ldarg_2 => 2,
                Code.Ldarg_3 => 3,
                _ when instruction.Operand is Parameter parameter => method.Parameters.IndexOf(parameter),
                _ => 0
            };

            if (!method.IsStatic)
            {
                if (index == 0)
                    return "this";
                index--;
            }

            if (index >= 0 && index < method.Parameters.Count)
                return string.IsNullOrWhiteSpace(method.Parameters[index].Name) ? $"arg{index}" : method.Parameters[index].Name;
            return $"arg{index}";
        }

        static int ResolveLocalIndex(MethodDef method, Instruction instruction)
        {
            if (instruction.Operand is Local local && method.Body != null)
                return method.Body.Variables.IndexOf(local);

            return instruction.OpCode.Code switch
            {
                Code.Ldloc_0 or Code.Stloc_0 => 0,
                Code.Ldloc_1 or Code.Stloc_1 => 1,
                Code.Ldloc_2 or Code.Stloc_2 => 2,
                Code.Ldloc_3 or Code.Stloc_3 => 3,
                _ => 0
            };
        }

        static string GetCurrentLocalSsaName(string[] locals, int[] localVersions, int index)
        {
            if (index < 0 || index >= locals.Length)
                return $"v{index}_?";
            return $"v{index}_{localVersions[index]}";
        }

        static string GetBinaryOperator(Code code) => code switch
        {
            Code.Add => "+",
            Code.Sub => "-",
            Code.Mul => "*",
            Code.Div => "/",
            Code.Rem => "%",
            Code.Xor => "^",
            Code.And => "&",
            Code.Or => "|",
            _ => code.ToString()
        };

        static string FormatOperandLiteral(object? operand) =>
            operand switch
            {
                null => "null",
                string text => $"\"{text}\"",
                _ => operand.ToString() ?? "<?>"
            };

        static int GetIntConstant(Instruction instruction) => instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4 or Code.Ldc_I4_S => Convert.ToInt32(instruction.Operand, System.Globalization.CultureInfo.InvariantCulture),
            _ => 0
        };

        /// <summary>
        /// Closes (removes) a specific assembly from dnSpy by name or file path.
        /// </summary>
        public CallToolResult CloseAssembly(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("assembly_name", out var nameObj))
                throw new ArgumentException("assembly_name is required");

            var assemblyName = nameObj.ToString() ?? string.Empty;
            string? filePath = arguments.TryGetValue("file_path", out var fpObj) ? fpObj?.ToString() : null;

            var normalizedPath = filePath?.Replace('/', '\\');
            var removed = System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Only top-level nodes (direct children of root) can be passed to Remove()
                var topLevel = documentTreeView.TreeView.Root.Children
                    .Select(n => n.Data as DsDocumentNode)
                    .Where(n => n != null)
                    .Cast<DsDocumentNode>();

                var toRemove = topLevel
                    .Where(node =>
                    {
                        var asm = node.Document?.AssemblyDef;
                        if (asm == null) return false;
                        bool nameMatch = asm.Name.String.Equals(assemblyName, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrWhiteSpace(normalizedPath))
                            return nameMatch && (node.Document!.Filename ?? "").Replace('/', '\\')
                                .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase);
                        return nameMatch;
                    })
                    .ToList();

                if (toRemove.Count == 0) return new List<string>();

                var paths = toRemove.Select(n => n.Document?.Filename ?? n.Document?.AssemblyDef?.FullName ?? "?").ToList();
                documentTreeView.Remove(toRemove);
                return paths;
            });

            if (removed.Count == 0)
                throw new ArgumentException($"Assembly not found: {assemblyName}. Use list_assemblies to see loaded assemblies.");

            var result = JsonSerializer.Serialize(new { Removed = removed, Count = removed.Count }, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = result } }
            };
        }

        /// <summary>
        /// Closes all assemblies currently loaded in dnSpy.
        /// </summary>
        public CallToolResult CloseAllAssemblies()
        {
            var removed = System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Only remove top-level nodes (direct children of root)
                var topLevel = documentTreeView.TreeView.Root.Children
                    .Select(n => n.Data as DsDocumentNode)
                    .Where(n => n != null)
                    .Cast<DsDocumentNode>()
                    .ToList();
                var paths = topLevel.Select(n => n.Document?.Filename ?? n.Document?.AssemblyDef?.FullName ?? "?").ToList();
                if (topLevel.Count > 0)
                    documentTreeView.Remove(topLevel);
                return paths;
            });

            var result = JsonSerializer.Serialize(new { Removed = removed, Count = removed.Count }, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = result } }
            };
        }
    }
}
