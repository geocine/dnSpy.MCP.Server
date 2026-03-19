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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Scripting;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Text;
using dnSpy.MCP.Server.Communication;
using dnSpy.MCP.Server.Contracts;
using dnSpy.MCP.Server.Application;
using dnSpy.MCP.Server.Helper;

namespace dnSpy.MCP.Server.Application
{
    [Export(typeof(McpTools))]
    public sealed partial class McpTools
    {
        readonly IServiceLocator serviceLocator;
        readonly IDocumentTreeView documentTreeView;
        readonly Lazy<AssemblyTools> assemblyTools;
        readonly Lazy<TypeTools> typeTools;
        readonly Lazy<EditTools> editTools;
        readonly Lazy<DebugTools> debugTools;
        readonly Lazy<DumpTools> dumpTools;
        readonly Lazy<MemoryInspectTools> memoryInspectTools;
        readonly Lazy<UsageFindingCommandTools> usageFindingTools;
        readonly Lazy<CodeAnalysisHelpers> codeAnalysisTools;
        readonly Lazy<De4dotTools> de4dotTools;
        readonly Lazy<ScriptTools> scriptTools;
        readonly Lazy<WindowTools> windowTools;

        [ImportingConstructor]
        public McpTools(IServiceLocator serviceLocator)
        {
            this.serviceLocator = serviceLocator;
            this.documentTreeView = serviceLocator.Resolve<IDocumentTreeView>();
            this.assemblyTools = CreateLazy<AssemblyTools>();
            this.typeTools = CreateLazy<TypeTools>();
            this.editTools = CreateLazy<EditTools>();
            this.debugTools = CreateLazy<DebugTools>();
            this.dumpTools = CreateLazy<DumpTools>();
            this.memoryInspectTools = CreateLazy<MemoryInspectTools>();
            this.usageFindingTools = CreateLazy<UsageFindingCommandTools>();
            this.codeAnalysisTools = CreateLazy<CodeAnalysisHelpers>();
            this.de4dotTools = CreateLazy<De4dotTools>();
            this.scriptTools = CreateLazy<ScriptTools>();
            this.windowTools = CreateLazy<WindowTools>();
        }

        // GetAvailableTools() is defined in McpTools.Schemas.cs (partial class)

        Lazy<T> CreateLazy<T>() where T : class =>
            new Lazy<T>(() => ResolveRequired<T>());

        T ResolveRequired<T>() where T : class {
            var service = serviceLocator.TryResolve<T>();
            if (service != null)
                return service;

            throw new InvalidOperationException($"{typeof(T).Name} is not available in the current dnSpy composition");
        }

        internal bool CanResolve<T>() where T : class => serviceLocator.TryResolve<T>() != null;

        internal ToolCatalog GetToolCatalog() =>
            ToolCatalog.Create(BuildLegacyToolList());

        internal bool IsToolCallable(string toolName) => toolName switch {
            "list_assemblies" or "select_assembly" or "close_assembly" or "close_all_assemblies" or
            "get_assembly_info" or "get_pe_info" or "normalize_member_id" or "resolve_member_id" or
            "get_member_details" or "get_decompiled_source" or "batch_get_decompiled_source" or "get_ast_outline" or
            "decompile_assembly" or "get_startup_map" or "get_resource_map" or
            "get_reconstruction_diagnostics" or "get_manifest_and_entrypoints" or "get_native_module_map" or "search_members" or "search_string_literals" or
            "get_user_strings" or "find_string_references" or "find_callees" or "get_method_xrefs" or
            "find_base_types" or "find_derived_types" or "get_implementations" or "get_overrides" or
            "find_usages" or "search_attributes" or "list_types" or "list_native_modules" or
            "get_native_imports" or "get_native_exports" or
            "scan_pe_strings" or "load_assembly" or "resolve_token" or "validate_assembly" or
            "list_metadata_tables" or "dump_metadata_heap" or "analyze_static_constructors" or
            "find_reflection_usage" or "suggest_symbol_renames" or "detect_anti_debug" or
            "detect_anti_tamper" or "find_proxy_methods" or "detect_string_encryption" or
            "find_delegate_creation" or "find_dynamic_code" or "find_byte_arrays" or
            "find_embedded_pes" or "analyze_control_flow" or "get_protection_report" or
            "triage" or "get_semantic_labels" or "get_cfg" or "match_framework_or_package" or
            "label_third_party_components" or "identify_known_binary" or "get_provenance_report" or
            "load_symbols" or "get_symbol_status" or "find_source_candidates" or
            "match_open_source_candidates" => CanResolve<AssemblyTools>(),

            "get_type_info" or "decompile_method" or "list_methods_in_type" or "search_methods" or "list_properties_in_type" or
            "get_method_signature" or "get_method_il" or "get_method_il_bytes" or
            "get_method_exception_handlers" or "get_type_fields" or "get_type_property" or
            "find_path_to_type" => CanResolve<TypeTools>(),

            "decompile_type" or "change_member_visibility" or "rename_member" or "rename_method" or "save_assembly" or
            "rename_symbol" or "rename_parameter" or "get_assembly_metadata" or "edit_assembly_metadata" or "list_assembly_attributes" or
            "remove_assembly_attribute" or "set_assembly_flags" or "list_assembly_references" or
            "add_assembly_reference" or "remove_assembly_reference" or "list_resources" or
            "get_resource" or "add_resource" or "remove_resource" or "extract_costura" or
            "inject_type_from_dll" or "list_pinvoke_methods" or "patch_method_to_ret" or
            "list_events_in_type" or "get_custom_attributes" or "list_nested_types" => CanResolve<EditTools>(),

            "list_runtime_modules" or "dump_module_from_memory" or "read_process_memory" or
            "write_process_memory" or "get_pe_sections" or "dump_pe_section" or
            "dump_module_unpacked" or "dump_memory_to_file" or "unpack_from_memory" or
            "dump_cordbg_il" => CanResolve<DumpTools>(),

            "get_local_variables" or "eval_expression" => CanResolve<MemoryInspectTools>(),

            "find_who_uses_type" or "find_who_reads_field" or "find_who_writes_field" =>
                CanResolve<UsageFindingCommandTools>(),

            "analyze_call_graph" or "find_dependency_chain" or "analyze_cross_assembly_dependencies" or
            "find_dead_code" => CanResolve<CodeAnalysisHelpers>(),

            "start_debugging" or "attach_to_process" or "get_debugger_state" or "list_breakpoints" or
            "set_breakpoint" or "set_tracepoint" or "remove_breakpoint" or "clear_all_breakpoints" or "continue_debugger" or
            "break_debugger" or "stop_debugging" or "get_call_stack" or "step_over" or
            "step_into" or "step_out" or "get_current_location" or "wait_for_pause" or
            "inspect_breakpoint" or "get_selected_node" or "get_active_tab" or "select_document_node" or
            "follow_reference" or
            "set_exception_breakpoint" or "remove_exception_breakpoint" or
            "list_exception_breakpoints" => CanResolve<DebugTools>(),

            "list_deobfuscators" or "detect_obfuscator" or "deobfuscate_assembly" or
            "save_deobfuscated" => CanResolve<De4dotTools>(),

            "run_script" => CanResolve<ScriptTools>(),
            "list_dialogs" or "close_dialog" => CanResolve<WindowTools>(),

            "search_types" or "find_who_calls_method" or "analyze_type_inheritance" or
            "list_tools" or "status" or "get_server_stats" or "get_logging_status" or "set_logging" or "search_tools" or
            "get_tool_schemas" or "list_tool_groups" or "enable_tool_groups" or
            "disable_tool_groups" or "get_enabled_tool_groups" => true,

            "get_code_mode_guide" or "get_code_examples" or "validate_code_snippet" or "execute_code" => CanResolve<ScriptTools>(),

            _ => true
        };

        public CallToolResult ExecuteTool(string toolName, Dictionary<string, object>? arguments, string? sessionId = null, bool bypassVisibilityForCodeMode = false)
        {
            if (!ToolNameMapper.TryMapPublicToInternal(toolName, out var internalToolName))
            {
                return ToolResponseFactory.Text($"Unknown tool: {toolName}", true, new {
                    tool_name = toolName,
                    error = "unknown_tool"
                });
            }

            try
            {
                var effectiveSessionId = ResolveEffectiveSessionId(sessionId, arguments);
                var stopwatch = Stopwatch.StartNew();
                var shouldLogToolCall = Configuration.McpConfig.Instance.EnableToolCallLogging;
                if (shouldLogToolCall)
                    McpLogger.Info($"Tool start public={toolName} internal={internalToolName} session={DescribeSessionMode(effectiveSessionId)} args={SummarizeToolArguments(internalToolName, arguments)}");

                if (!bypassVisibilityForCodeMode && !IsToolVisible(internalToolName, effectiveSessionId))
                {
                    var hiddenResult = CreateHiddenToolError(toolName);
                    if (shouldLogToolCall)
                        McpLogger.Warning($"Tool hidden public={toolName} internal={internalToolName} session={DescribeSessionMode(effectiveSessionId)}");
                    return hiddenResult;
                }

                if (bypassVisibilityForCodeMode && (internalToolName == "execute_code" || internalToolName == "run_script"))
                    return ToolResponseFactory.Json(new {
                        error = "tool_not_allowed_in_code_mode",
                        tool_name = toolName,
                        message = $"{toolName} cannot be invoked from dnspy_execute_code."
                    }, true);

                var result = internalToolName switch
                {
                    "list_tools" => ListTools(effectiveSessionId),
                    "status" => HandleDnspyStatus(),
                    "get_server_stats" => HandleDnspyGetServerStats(),
                    "get_logging_status" => HandleDnspyGetLoggingStatus(),
                    "set_logging" => HandleDnspySetLogging(arguments),
                    "get_code_mode_guide" => scriptTools.Value.GetCodeModeGuide(arguments),
                    "get_code_examples" => scriptTools.Value.GetCodeExamples(arguments),
                    "validate_code_snippet" => scriptTools.Value.ValidateCodeSnippet(arguments),
                    "search_tools" => HandleSearchTools(arguments, effectiveSessionId),
                    "get_tool_schemas" => HandleGetToolSchemas(arguments, effectiveSessionId),
                    "list_tool_groups" => HandleListToolGroups(effectiveSessionId),
                    "enable_tool_groups" => HandleEnableToolGroups(arguments, effectiveSessionId),
                    "disable_tool_groups" => HandleDisableToolGroups(arguments, effectiveSessionId),
                    "get_enabled_tool_groups" => HandleGetEnabledToolGroups(arguments, effectiveSessionId),
                    "execute_code" => scriptTools.Value.ExecuteCode(arguments, effectiveSessionId, ExecuteTool),
                    "list_assemblies"      => InvokeLazy(assemblyTools, "ListAssemblies",      null),
                    "select_assembly"      => InvokeLazy(assemblyTools, "SelectAssembly",      arguments),
                    "close_assembly"       => InvokeLazy(assemblyTools, "CloseAssembly",       arguments),
                    "close_all_assemblies" => InvokeLazy(assemblyTools, "CloseAllAssemblies",  null),
                    "get_assembly_info"    => InvokeLazy(assemblyTools, "GetAssemblyInfo",     arguments),
                    "get_pe_info"          => InvokeLazy(assemblyTools, "GetPeInfo",           arguments),
                    "validate_assembly"    => InvokeLazy(assemblyTools, "ValidateAssembly",    arguments),
                    "list_metadata_tables" => InvokeLazy(assemblyTools, "ListMetadataTables",  arguments),
                    "dump_metadata_heap"   => InvokeLazy(assemblyTools, "DumpMetadataHeap",    arguments),
                    "normalize_member_id"  => InvokeLazy(assemblyTools, "NormalizeMemberId",   arguments),
                    "resolve_member_id"    => InvokeLazy(assemblyTools, "ResolveMemberId",     arguments),
                    "get_member_details"   => InvokeLazy(assemblyTools, "GetMemberDetails",    arguments),
                    "get_decompiled_source" => InvokeLazy(assemblyTools, "GetDecompiledSource", arguments),
                    "batch_get_decompiled_source" => InvokeLazy(assemblyTools, "BatchGetDecompiledSource", arguments),
                    "get_ast_outline" => InvokeLazy(assemblyTools, "GetAstOutline", arguments),
                    "decompile_assembly"   => InvokeLazy(assemblyTools, "DecompileAssembly",   arguments),
                    "export_to_project"    => InvokeLazy(assemblyTools, "ExportToProject",    arguments),
                    "get_startup_map"      => InvokeLazy(assemblyTools, "GetStartupMap",       arguments),
                    "get_resource_map"     => InvokeLazy(assemblyTools, "GetResourceMap",      arguments),
                    "decompile_baml"       => InvokeLazy(assemblyTools, "DecompileBaml",       arguments),
                    "export_xaml_resources" => InvokeLazy(assemblyTools, "ExportXamlResources", arguments),
                    "list_resource_elements" => InvokeLazy(assemblyTools, "ListResourceElements", arguments),
                    "extract_resx_bundle"   => InvokeLazy(assemblyTools, "ExtractResxBundle", arguments),
                    "get_reconstruction_diagnostics" => InvokeLazy(assemblyTools, "GetReconstructionDiagnostics", arguments),
                    "get_manifest_and_entrypoints" => InvokeLazy(assemblyTools, "GetManifestAndEntrypoints", arguments),
                    "get_native_module_map" => InvokeLazy(assemblyTools, "GetNativeModuleMap", arguments),
                    "analyze_static_constructors" => InvokeLazy(assemblyTools, "AnalyzeStaticConstructors", arguments),
                    "search_members"       => InvokeLazy(assemblyTools, "SearchMembers",       arguments),
                    "search_string_literals" => InvokeLazy(assemblyTools, "SearchStringLiterals", arguments),
                    "get_user_strings"     => InvokeLazy(assemblyTools, "GetUserStrings",      arguments),
                    "find_string_references" => InvokeLazy(assemblyTools, "FindStringReferences", arguments),
                    "find_reflection_usage" => InvokeLazy(assemblyTools, "FindReflectionUsage", arguments),
                    "suggest_symbol_renames" => InvokeLazy(assemblyTools, "SuggestSymbolRenames", arguments),
                    "detect_anti_debug" => InvokeLazy(assemblyTools, "DetectAntiDebug", arguments),
                    "detect_anti_tamper" => InvokeLazy(assemblyTools, "DetectAntiTamper", arguments),
                    "find_proxy_methods" => InvokeLazy(assemblyTools, "FindProxyMethods", arguments),
                    "detect_string_encryption" => InvokeLazy(assemblyTools, "DetectStringEncryption", arguments),
                    "find_delegate_creation" => InvokeLazy(assemblyTools, "FindDelegateCreation", arguments),
                    "find_dynamic_code" => InvokeLazy(assemblyTools, "FindDynamicCode", arguments),
                    "find_byte_arrays" => InvokeLazy(assemblyTools, "FindByteArrays", arguments),
                    "find_embedded_pes" => InvokeLazy(assemblyTools, "FindEmbeddedPes", arguments),
                    "analyze_control_flow" => InvokeLazy(assemblyTools, "AnalyzeControlFlow", arguments),
                    "get_cfg" => InvokeLazy(assemblyTools, "GetCfg", arguments),
                    "get_ssa" => InvokeLazy(assemblyTools, "GetSsa", arguments),
                    "emulate_method" => InvokeLazy(assemblyTools, "EmulateMethod", arguments),
                    "get_protection_report" => InvokeLazy(assemblyTools, "GetProtectionReport", arguments),
                    "triage" => InvokeLazy(assemblyTools, "Triage", arguments),
                    "get_semantic_labels" => InvokeLazy(assemblyTools, "GetSemanticLabels", arguments),
                    "match_framework_or_package" => InvokeLazy(assemblyTools, "MatchFrameworkOrPackage", arguments),
                    "label_third_party_components" => InvokeLazy(assemblyTools, "LabelThirdPartyComponents", arguments),
                    "identify_known_binary" => InvokeLazy(assemblyTools, "IdentifyKnownBinary", arguments),
                    "get_provenance_report" => InvokeLazy(assemblyTools, "GetProvenanceReport", arguments),
                    "load_symbols" => InvokeLazy(assemblyTools, "LoadSymbols", arguments),
                    "get_symbol_status" => InvokeLazy(assemblyTools, "GetSymbolStatus", arguments),
                    "find_source_candidates" => InvokeLazy(assemblyTools, "FindSourceCandidates", arguments),
                    "match_open_source_candidates" => InvokeLazy(assemblyTools, "MatchOpenSourceCandidates", arguments),
                    "find_callees"         => InvokeLazy(assemblyTools, "FindCallees",         arguments),
                    "get_method_xrefs"     => InvokeLazy(assemblyTools, "GetMethodXrefs",      arguments),
                    "find_base_types"      => InvokeLazy(assemblyTools, "FindBaseTypes",       arguments),
                    "find_derived_types"   => InvokeLazy(assemblyTools, "FindDerivedTypes",    arguments),
                    "get_implementations"  => InvokeLazy(assemblyTools, "GetImplementations",  arguments),
                    "get_overrides"        => InvokeLazy(assemblyTools, "GetOverrides",        arguments),
                    "find_usages"         => InvokeLazy(assemblyTools, "FindUsages",          arguments),
                    "search_attributes"   => InvokeLazy(assemblyTools, "SearchAttributes",    arguments),
                    "resolve_token"        => InvokeLazy(assemblyTools, "ResolveToken",        arguments),
                    "list_types" => InvokeLazy(assemblyTools, "ListTypes", arguments),
                    "get_type_info" => InvokeLazy(typeTools, "GetTypeInfo", arguments),
                    "decompile_method" => InvokeLazy(typeTools, "DecompileMethod", arguments),
                    "list_methods_in_type" => InvokeLazy(typeTools, "ListMethodsInType", arguments),
                    "search_methods" => InvokeLazy(typeTools, "SearchMethods", arguments),
                    "list_properties_in_type" => InvokeLazy(typeTools, "ListPropertiesInType", arguments),
                    "get_method_signature" => InvokeLazy(typeTools, "GetMethodSignature", arguments),
                    "search_types" => SearchTypes(arguments),
                    "find_who_calls_method" => FindWhoCallsMethod(arguments),
                    "analyze_type_inheritance" => AnalyzeTypeInheritance(arguments),
                    "get_method_il" => InvokeLazy(typeTools, "GetMethodIL", arguments),
                    "get_method_il_bytes" => InvokeLazy(typeTools, "GetMethodILBytes", arguments),
                    "get_method_exception_handlers" => InvokeLazy(typeTools, "GetMethodExceptionHandlers", arguments),

                    // Edit tools
                    "decompile_type" => InvokeLazy(editTools, "DecompileType", arguments),
                    "change_member_visibility" => InvokeLazy(editTools, "ChangeVisibility", arguments),
                    "rename_member" => InvokeLazy(editTools, "RenameMember", arguments),
                    "rename_method" => InvokeLazy(editTools, "RenameMethod", arguments),
                    "rename_symbol" => InvokeLazy(editTools, "RenameSymbol", arguments),
                    "rename_parameter" => InvokeLazy(editTools, "RenameParameter", arguments),
                    "save_assembly" => InvokeLazy(editTools, "SaveAssembly", arguments),
                    "get_assembly_metadata" => InvokeLazy(editTools, "GetAssemblyMetadata", arguments),
                    "edit_assembly_metadata" => InvokeLazy(editTools, "EditAssemblyMetadata", arguments),
                    "list_assembly_attributes"  => InvokeLazy(editTools, "ListAssemblyAttributes",  arguments),
                    "remove_assembly_attribute" => InvokeLazy(editTools, "RemoveAssemblyAttribute", arguments),
                    "set_assembly_flags" => InvokeLazy(editTools, "SetAssemblyFlags", arguments),
                    "list_assembly_references"  => InvokeLazy(editTools, "ListAssemblyReferences",  arguments),
                    "add_assembly_reference"    => InvokeLazy(editTools, "AddAssemblyReference",    arguments),
                    "remove_assembly_reference" => InvokeLazy(editTools, "RemoveAssemblyReference", arguments),
                    "list_resources"            => InvokeLazy(editTools, "ListResources",            arguments),
                    "get_resource"              => InvokeLazy(editTools, "GetResource",              arguments),
                    "add_resource"              => InvokeLazy(editTools, "AddResource",              arguments),
                    "remove_resource"           => InvokeLazy(editTools, "RemoveResource",           arguments),
                    "extract_costura"           => InvokeLazy(editTools, "ExtractCostura",           arguments),
                    "inject_type_from_dll"      => InvokeLazy(editTools, "InjectTypeFromDll",        arguments),
                    "list_pinvoke_methods" => InvokeLazy(editTools, "ListPInvokeMethods", arguments),
                    "patch_method_to_ret" => InvokeLazy(editTools, "PatchMethodToRet", arguments),
                    "list_events_in_type" => InvokeLazy(editTools, "ListEventsInType", arguments),
                    "get_custom_attributes" => InvokeLazy(editTools, "GetCustomAttributes", arguments),
                    "list_nested_types" => InvokeLazy(editTools, "ListNestedTypes", arguments),

                    // Previously-hidden TypeTools
                    "get_type_fields" => InvokeLazy(typeTools, "GetTypeFields", arguments),
                    "get_type_property" => InvokeLazy(typeTools, "GetTypeProperty", arguments),
                    "find_path_to_type" => InvokeLazy(typeTools, "FindPathToType", arguments),
                    "list_native_modules" => InvokeLazy(assemblyTools, "ListNativeModules", arguments),
                    "get_native_imports" => InvokeLazy(assemblyTools, "GetNativeImports", arguments),
                    "get_native_exports" => InvokeLazy(assemblyTools, "GetNativeExports", arguments),

                    // Memory dump tools
                    "list_runtime_modules" => InvokeLazy(dumpTools, "ListRuntimeModules", arguments),
                    "dump_module_from_memory" => InvokeLazy(dumpTools, "DumpModuleFromMemory", arguments),
                    "read_process_memory"  => InvokeLazy(dumpTools, "ReadProcessMemory",  arguments),
                    "write_process_memory" => InvokeLazy(dumpTools, "WriteProcessMemory", arguments),
                    "get_pe_sections" => InvokeLazy(dumpTools, "GetPeSections", arguments),
                    "dump_pe_section" => InvokeLazy(dumpTools, "DumpPeSection", arguments),
                    "dump_module_unpacked" => InvokeLazy(dumpTools, "DumpModuleUnpacked", arguments),
                    "dump_memory_to_file" => InvokeLazy(dumpTools, "DumpMemoryToFile", arguments),

                    // Memory inspect / runtime variable tools
                    "get_local_variables" => InvokeLazy(memoryInspectTools, "GetLocalVariables", arguments),
                    "eval_expression"     => InvokeLazy(memoryInspectTools, "EvalExpression",    arguments),

                    // Usage finding tools
                    "find_who_uses_type"   => InvokeLazy(usageFindingTools, "FindWhoUsesTypeArgs",   arguments),
                    "find_who_reads_field" => InvokeLazy(usageFindingTools, "FindWhoReadsFieldArgs", arguments),
                    "find_who_writes_field" => InvokeLazy(usageFindingTools, "FindWhoWritesFieldArgs", arguments),

                    // Code analysis tools
                    "analyze_call_graph"                    => InvokeLazy(codeAnalysisTools, "AnalyzeCallGraphArgs",                    arguments),
                    "find_dependency_chain"                 => InvokeLazy(codeAnalysisTools, "FindDependencyChainArgs",                 arguments),
                    "analyze_cross_assembly_dependencies"   => InvokeLazy(codeAnalysisTools, "AnalyzeCrossAssemblyDependenciesArgs",   arguments),
                    "find_dead_code"                        => InvokeLazy(codeAnalysisTools, "FindDeadCodeArgs",                        arguments),

                    // PE / string scanning tools
                    "scan_pe_strings" => InvokeLazy(assemblyTools, "ScanPeStrings", arguments),

                    // Assembly loading
                    "load_assembly"    => InvokeLazy(assemblyTools, "LoadAssembly",    arguments),

                    // Process launch / attach / unpack tools
                    "start_debugging"    => InvokeLazy(debugTools, "StartDebugging",  arguments),
                    "attach_to_process"  => InvokeLazy(debugTools, "AttachToProcess", arguments),
                    "unpack_from_memory" => InvokeLazy(dumpTools,  "UnpackFromMemory", arguments),
                    "dump_cordbg_il"     => InvokeLazy(dumpTools,  "DumpCordbgIL",    arguments),

                    // Debug tools
                    "get_debugger_state" => InvokeLazy(debugTools, "GetDebuggerState", arguments),
                    "list_breakpoints" => InvokeLazy(debugTools, "ListBreakpoints", arguments),
                    "inspect_breakpoint" => InvokeLazy(debugTools, "InspectBreakpoint", arguments),
                    "set_breakpoint" => InvokeLazy(debugTools, "SetBreakpoint", arguments),
                    "set_tracepoint" => InvokeLazy(debugTools, "SetTracepoint", arguments),
                    "remove_breakpoint" => InvokeLazy(debugTools, "RemoveBreakpoint", arguments),
                    "clear_all_breakpoints" => InvokeLazy(debugTools, "ClearAllBreakpoints", arguments),
                    "continue_debugger" => InvokeLazy(debugTools, "ContinueDebugger", arguments),
                    "break_debugger" => InvokeLazy(debugTools, "BreakDebugger", arguments),
                    "stop_debugging" => InvokeLazy(debugTools, "StopDebugging", arguments),
                    "get_call_stack" => InvokeLazy(debugTools, "GetCallStack", arguments),
                    "get_selected_node" => InvokeLazy(debugTools, "GetSelectedNode", arguments),
                    "get_active_tab" => InvokeLazy(debugTools, "GetActiveTab", arguments),
                    "select_document_node" => InvokeLazy(debugTools, "SelectDocumentNode", arguments),
                    "follow_reference" => InvokeLazy(debugTools, "FollowReference", arguments),
                    "get_tool_window_state" => InvokeLazy(debugTools, "GetToolWindowState", arguments),
                    "focus_debugger_context" => InvokeLazy(debugTools, "FocusDebuggerContext", arguments),

                    "step_over"            => InvokeLazy(debugTools, "StepOver",           arguments),
                    "step_into"            => InvokeLazy(debugTools, "StepInto",           arguments),
                    "step_out"             => InvokeLazy(debugTools, "StepOut",            arguments),
                    "get_current_location" => InvokeLazy(debugTools, "GetCurrentLocation", arguments),
                    "wait_for_pause"       => InvokeLazy(debugTools, "WaitForPause",       arguments),

                    "set_exception_breakpoint"    => InvokeLazy(debugTools, "SetExceptionBreakpoint",    arguments),
                    "remove_exception_breakpoint" => InvokeLazy(debugTools, "RemoveExceptionBreakpoint", arguments),
                    "list_exception_breakpoints"  => InvokeLazy(debugTools, "ListExceptionBreakpoints",  arguments),

                    // de4dot deobfuscation tools
                    "list_deobfuscators"    => InvokeLazy(de4dotTools, "ListDeobfuscators",    arguments),
                    "detect_obfuscator"     => InvokeLazy(de4dotTools, "DetectObfuscator",     arguments),
                    "deobfuscate_assembly"  => InvokeLazy(de4dotTools, "DeobfuscateAssembly",  arguments),
                    "save_deobfuscated"     => InvokeLazy(de4dotTools, "SaveDeobfuscated",     arguments),

                    // Roslyn scripting
                    "run_script" => InvokeLazy(scriptTools, "RunScript", arguments),

                    // Window / dialog management
                    "list_dialogs" => InvokeLazy(windowTools, "ListDialogs", arguments),
                    "close_dialog" => InvokeLazy(windowTools, "CloseDialog", arguments),

                    _ => new CallToolResult
                    {
                        Content = new List<ToolContent> {
                            new ToolContent { Text = $"Unknown tool: {toolName}" }
                        },
                        IsError = true
                    }
                };

                stopwatch.Stop();
                if (shouldLogToolCall)
                    McpLogger.Info($"Tool end public={toolName} internal={internalToolName} session={DescribeSessionMode(effectiveSessionId)} elapsed_ms={stopwatch.ElapsedMilliseconds} is_error={result.IsError}");
                return result;
            }
            catch (Exception ex)
            {
                McpLogger.Exception(ex, $"Error executing tool {toolName}");
                return new CallToolResult
                {
                    Content = new List<ToolContent> {
                        new ToolContent { Text = $"Error executing tool {toolName}: {ex.Message}" }
                    },
                    IsError = true
                };
            }
        }

        CallToolResult ListTools(string? sessionId)
        {
            return HandleDnspyListTools(sessionId);
        }

        CallToolResult HandleDnspyListTools(string? sessionId)
        {
            var tools = GetAvailableTools(sessionId);
            var catalog = GetToolCatalog();
            var payload = new {
                tools,
                total_count = tools.Count,
                full_catalog_count = catalog.Tools.Count,
                stats = catalog.GetStats(),
                enabled_groups = HandleGetEnabledToolGroups(null, sessionId).StructuredContent
            };
            return ToolResponseFactory.Json(payload);
        }

        CallToolResult InvokeLazy<T>(Lazy<T> lazy, string methodName, Dictionary<string, object>? arguments) where T : class
        {
            if (lazy == null)
                throw new ArgumentNullException(nameof(lazy));
            try
            {
                object? instance;
                try
                {
                    instance = lazy.Value;
                }
                catch (Exception ex)
                {
                    McpLogger.Exception(ex, "InvokeLazy: failed to construct lazy.Value");
                    return new CallToolResult
                    {
                        Content = new List<ToolContent> { new ToolContent { Text = $"InvokeLazy construction error: {ex.Message}" } },
                        IsError = true
                    };
                }

                if (instance == null)
                {
                    return new CallToolResult
                    {
                        Content = new List<ToolContent> { new ToolContent { Text = "InvokeLazy: instance is null" } },
                        IsError = true
                    };
                }

                var type = instance.GetType();
                var mi = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (mi == null)
                {
                    return new CallToolResult
                    {
                        Content = new List<ToolContent> { new ToolContent { Text = $"Method not found on {type.FullName}: {methodName}" } },
                        IsError = true
                    };
                }

                var parameters = mi.GetParameters();
                object?[] invokeArgs;

                if (parameters.Length == 0) {
                    invokeArgs = Array.Empty<object?>();
                }
                else {
                    // Pass arguments (null or empty dict) — the callee validates required params itself
                    invokeArgs = new object?[] { arguments };
                }

                var res = mi.Invoke(instance, invokeArgs);
                if (res is CallToolResult ctr)
                    return ctr;

                var text = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true });
                return new CallToolResult { Content = new List<ToolContent> { new ToolContent { Text = text } } };
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                McpLogger.Exception(inner, $"InvokeLazy error in {methodName}");
                return new CallToolResult
                {
                    Content = new List<ToolContent> { new ToolContent { Text = inner is ArgumentException
                        ? $"Parameter error: {inner.Message}"
                        : $"Tool error: {inner.Message}" } },
                    IsError = true
                };
            }
            catch (Exception ex)
            {
                McpLogger.Exception(ex, "InvokeLazy failed");
                return new CallToolResult
                {
                    Content = new List<ToolContent> { new ToolContent { Text = $"InvokeLazy error: {ex.Message}" } },
                    IsError = true
                };
            }
        }

        dnlib.DotNet.AssemblyDef? FindAssemblyByName(string name)
        {
            return UiThreadHelper.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(m => m.Document?.AssemblyDef)
                    .FirstOrDefault(a => a?.Name.String.Equals(name, StringComparison.OrdinalIgnoreCase) == true));
        }

        dnlib.DotNet.TypeDef? FindTypeInAssembly(dnlib.DotNet.AssemblyDef assembly, string typeFullName)
        {
            return assembly.Modules
                .SelectMany(m => m.Types)
                .FirstOrDefault(t => t.FullName.Equals(typeFullName, StringComparison.OrdinalIgnoreCase));
        }

        CallToolResult SearchTypes(Dictionary<string, object>? arguments)
        {
            var query = RequireString(arguments, "query");

            string? cursor = null;
            if (arguments?.TryGetValue("cursor", out var cursorObj) == true)
                cursor = cursorObj?.ToString();

            var (offset, pageSize) = DecodeCursor(cursor);

            bool hasPattern = query.IndexOfAny(new[] { '*', '?', '^', '$', '[', '(', '|', '+', '{' }) >= 0;
            System.Text.RegularExpressions.Regex? regex = hasPattern ? BuildPatternRegex(query) : null;

            var assemblies = UiThreadHelper.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(m => m.Document?.AssemblyDef)
                    .Where(a => a != null)
                    .ToList());

            var results = assemblies
                .SelectMany(a => a!.Modules.SelectMany(mod => GetAllTypesRecursive(mod.Types)))
                .Where(t => {
                    if (regex != null)
                        return regex.IsMatch(t.FullName);
                    return t.FullName.Contains(query, StringComparison.OrdinalIgnoreCase);
                })
                .Select(t => new
                {
                    FullName = t.FullName,
                    Namespace = t.Namespace.String,
                    Name = t.Name.String,
                    AssemblyName = t.Module.Assembly?.Name.String ?? "Unknown"
                })
                .ToList();

            return CreatePaginatedJsonResponse(results, offset, pageSize);
        }

        static IEnumerable<dnlib.DotNet.TypeDef> GetAllTypesRecursive(IEnumerable<dnlib.DotNet.TypeDef> types)
        {
            foreach (var type in types)
            {
                yield return type;
                foreach (var nested in GetAllTypesRecursive(type.NestedTypes))
                    yield return nested;
            }
        }

        CallToolResult FindWhoCallsMethod(Dictionary<string, object>? arguments)
        {
            var asmName = RequireString(arguments, "assembly_name");
            var typeName = RequireString(arguments, "type_full_name");
            var methodNameStr = RequireString(arguments, "method_name");

            var assembly = FindAssemblyByName(asmName);
            if (assembly == null)
                throw new ArgumentException($"Assembly not found: {asmName}");

            var type = FindTypeInAssembly(assembly, typeName);
            if (type == null)
                throw new ArgumentException($"Type not found: {typeName}");

            var targetMethod = type.Methods.FirstOrDefault(m => m.Name.String == methodNameStr);
            if (targetMethod == null)
                throw new ArgumentException($"Method not found: {methodNameStr}");

            // Collect assembly references on the UI thread (documentTreeView is WPF-bound)
            var targetFullName = targetMethod.FullName;
            var assemblies = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(m => m.Document?.AssemblyDef)
                    .Where(a => a != null)
                    .ToList());

            // IL scan runs on the background thread — dnlib objects are not WPF-bound
            var callers = assemblies
                .SelectMany(a => a!.Modules)
                .SelectMany(mod => GetAllTypesRecursive(mod))
                .SelectMany(t => t.Methods)
                .Where(m => m.Body?.Instructions != null)
                .SelectMany(m => m.Body.Instructions
                    .Where(instr =>
                        (instr.OpCode.Code == dnlib.DotNet.Emit.Code.Call ||
                         instr.OpCode.Code == dnlib.DotNet.Emit.Code.Callvirt) &&
                        instr.Operand is MethodDef calledDef && calledDef.FullName == targetFullName)
                    .Select(_ => new {
                        MethodName = m.Name.String,
                        DeclaringType = m.DeclaringType?.FullName ?? "Unknown",
                        AssemblyName = m.DeclaringType?.Module?.Assembly?.Name.String ?? "Unknown"
                    }))
                .OrderBy(c => c.AssemblyName).ThenBy(c => c.DeclaringType).ThenBy(c => c.MethodName)
                .ToList();

            var resultJson = System.Text.Json.JsonSerializer.Serialize(new {
                TargetMethod = targetFullName,
                CallerCount = callers.Count,
                Callers = callers
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult {
                Content = new List<ToolContent> { new ToolContent { Text = resultJson } }
            };
        }

        IEnumerable<TypeDef> GetAllTypesRecursive(ModuleDef module)
        {
            foreach (var type in module.Types)
            {
                yield return type;
                foreach (var nested in GetAllNestedTypesRecursive(type))
                    yield return nested;
            }
        }

        IEnumerable<TypeDef> GetAllNestedTypesRecursive(TypeDef type)
        {
            foreach (var nested in type.NestedTypes)
            {
                yield return nested;
                foreach (var deep in GetAllNestedTypesRecursive(nested))
                    yield return deep;
            }
        }

        CallToolResult AnalyzeTypeInheritance(Dictionary<string, object>? arguments)
        {
            var asmName = RequireString(arguments, "assembly_name");
            var typeName = RequireString(arguments, "type_full_name");

            var assembly = FindAssemblyByName(asmName);
            if (assembly == null)
                throw new ArgumentException($"Assembly not found: {asmName}");

            var type = FindTypeInAssembly(assembly, typeName);
            if (type == null)
                throw new ArgumentException($"Type not found: {typeName}");

            var baseClasses = new List<string>();
            var currentType = type.BaseType;
            while (currentType != null && currentType.FullName != "System.Object")
            {
                baseClasses.Add(currentType.FullName);
                var typeDef = currentType.ResolveTypeDef();
                currentType = typeDef?.BaseType;
            }

            var interfaces = type.Interfaces.Select(i => i.Interface.FullName).ToList();

            var result = JsonSerializer.Serialize(new
            {
                Type = type.FullName,
                BaseClasses = baseClasses,
                Interfaces = interfaces
            }, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = result } }
            };
        }

        static (int offset, int pageSize) DecodeCursor(string? cursor)
        {
            if (string.IsNullOrEmpty(cursor))
                return (0, 50);

            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(cursor));
                var parts = decoded.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var offset) && int.TryParse(parts[1], out var pageSize))
                    return (offset, pageSize);
            }
            catch { }
            return (0, 10);
        }

        static string EncodeCursor(int offset, int pageSize)
        {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{offset}:{pageSize}"));
        }

        static System.Text.RegularExpressions.Regex BuildPatternRegex(string pattern)
        {
            bool isRegex = pattern.IndexOfAny(new[] { '^', '$', '[', '(', '|', '+', '{' }) >= 0;
            if (isRegex)
            {
                return new System.Text.RegularExpressions.Regex(
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            }

            var escaped = System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".");
            return new System.Text.RegularExpressions.Regex(
                "^" + escaped + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        static CallToolResult CreatePaginatedJsonResponse<T>(List<T> items, int offset, int pageSize)
        {
            var pagedItems = items.Skip(offset).Take(pageSize).ToList();
            var hasMore = offset + pageSize < items.Count;

            var result = new Dictionary<string, object>
            {
                ["items"] = pagedItems,
                ["total_count"] = items.Count,
                ["returned_count"] = pagedItems.Count,
                ["offset"] = offset
            };

            if (hasMore)
                result["nextCursor"] = EncodeCursor(offset + pageSize, pageSize);

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = json } }
            };
        }

        // ── Parameter helpers ─────────────────────────────────────────────────
        static string RequireString(Dictionary<string, object>? args, string key)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null)
                throw new ArgumentException($"Missing required parameter: '{key}'");
            var s = v.ToString();
            if (string.IsNullOrWhiteSpace(s))
                throw new ArgumentException($"Parameter '{key}' cannot be empty");
            return s!;
        }

        static string? OptionalString(Dictionary<string, object>? args, string key, string? def = null)
        {
            if (args == null || !args.TryGetValue(key, out var v)) return def;
            return v?.ToString() ?? def;
        }

        static int OptionalInt(Dictionary<string, object>? args, string key, int def = 0)
        {
            if (args == null || !args.TryGetValue(key, out var v)) return def;
            if (v is System.Text.Json.JsonElement je && je.TryGetInt32(out var ji)) return ji;
            return int.TryParse(v?.ToString(), out var i) ? i : def;
        }

        static bool OptionalBool(Dictionary<string, object>? args, string key, bool def = false)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null)
                return def;

            if (v is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True)
                    return true;
                if (je.ValueKind == JsonValueKind.False)
                    return false;
                if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var parsedFromJson))
                    return parsedFromJson;
            }

            return bool.TryParse(v.ToString(), out var parsed) ? parsed : def;
        }

        CallToolResult HandleDnspyStatus()
        {
            var server = serviceLocator.TryResolve<McpServer>();
            var catalog = GetToolCatalog();
            var config = Configuration.McpConfig.Instance;
            var visibleTools = GetAvailableTools(null);
            var payload = new {
                server_name = McpBuildInfo.ServerName,
                version = McpBuildInfo.Version,
                is_running = server?.IsRunning ?? false,
                status_message = server?.GetStatusMessage() ?? "Server is not available in the current dnSpy composition",
                registered_tool_count = catalog.Tools.Count,
                visible_tool_count = visibleTools.Count,
                discovery_mode = config.ExposeFullToolCatalog ? "full_catalog_compat" : "bootstrap_meta",
                implicit_default_session_enabled = config.AllowImplicitDefaultSession,
                implicit_default_session_id = config.AllowImplicitDefaultSession ? config.ImplicitDefaultSessionId : string.Empty,
                logging = BuildLoggingStatusPayload(),
                representative_tools = visibleTools.Select(a => a.Name).Take(5).ToList()
            };
            return ToolResponseFactory.Json(payload);
        }

        CallToolResult HandleDnspyGetServerStats()
        {
            var config = Configuration.McpConfig.Instance;
            var payload = new {
                version = McpBuildInfo.Version,
                tool_stats = GetToolCatalog().GetStats(),
                discovery = new {
                    expose_full_tool_catalog = config.ExposeFullToolCatalog,
                    allow_implicit_default_session = config.AllowImplicitDefaultSession,
                    implicit_default_session_id = config.AllowImplicitDefaultSession ? config.ImplicitDefaultSessionId : string.Empty,
                    enabled_session_count = GetEnabledSessionCount()
                },
                logging = BuildLoggingStatusPayload(),
                service_availability = new Dictionary<string, bool> {
                    ["assembly_tools"] = CanResolve<AssemblyTools>(),
                    ["type_tools"] = CanResolve<TypeTools>(),
                    ["edit_tools"] = CanResolve<EditTools>(),
                    ["debug_tools"] = CanResolve<DebugTools>(),
                    ["dump_tools"] = CanResolve<DumpTools>(),
                    ["memory_inspect_tools"] = CanResolve<MemoryInspectTools>(),
                    ["usage_finding_tools"] = CanResolve<UsageFindingCommandTools>(),
                    ["code_analysis_tools"] = CanResolve<CodeAnalysisHelpers>(),
                    ["de4dot_tools"] = CanResolve<De4dotTools>(),
                    ["script_tools"] = CanResolve<ScriptTools>(),
                    ["window_tools"] = CanResolve<WindowTools>()
                }
            };
            return ToolResponseFactory.Json(payload);
        }

        CallToolResult HandleDnspyGetLoggingStatus()
        {
            return ToolResponseFactory.Json(BuildLoggingStatusPayload());
        }

        CallToolResult HandleDnspySetLogging(Dictionary<string, object>? arguments)
        {
            var config = Configuration.McpConfig.Instance;
            var persist = OptionalBool(arguments, "persist", true);
            var requestedLogLevel = OptionalString(arguments, "log_level");

            if (!string.IsNullOrWhiteSpace(requestedLogLevel))
            {
                if (!Enum.TryParse<McpLogger.LogLevel>(requestedLogLevel, true, out var parsedLevel))
                    throw new ArgumentException("Invalid log_level. Expected one of: Debug, Info, Warning, Error.");
                config.LogLevel = parsedLevel.ToString();
            }

            if (arguments != null && arguments.ContainsKey("enable_file_logging"))
                config.EnableFileLogging = OptionalBool(arguments, "enable_file_logging", config.EnableFileLogging);
            if (arguments != null && arguments.ContainsKey("enable_output_pane_logging"))
                config.EnableOutputPaneLogging = OptionalBool(arguments, "enable_output_pane_logging", config.EnableOutputPaneLogging);
            if (arguments != null && arguments.ContainsKey("enable_tool_call_logging"))
                config.EnableToolCallLogging = OptionalBool(arguments, "enable_tool_call_logging", config.EnableToolCallLogging);

            if (persist)
                config.Save();

            return ToolResponseFactory.Json(new {
                updated = true,
                persisted = persist,
                logging = BuildLoggingStatusPayload()
            });
        }

        object BuildLoggingStatusPayload()
        {
            var config = Configuration.McpConfig.Instance;
            return new {
                log_level = config.LogLevel,
                enable_file_logging = config.EnableFileLogging,
                enable_output_pane_logging = config.EnableOutputPaneLogging,
                enable_tool_call_logging = config.EnableToolCallLogging,
                log_file_path = config.EnableFileLogging ? McpLogger.LogFilePath : string.Empty
            };
        }

        int GetEnabledSessionCount()
        {
            lock (enabledToolGroupsLock)
                return enabledToolGroupsBySession.Count;
        }

        static string DescribeSessionMode(string? sessionId)
        {
            var config = Configuration.McpConfig.Instance;
            if (string.IsNullOrWhiteSpace(sessionId))
                return "none";
            if (config.AllowImplicitDefaultSession &&
                string.Equals(sessionId, config.ImplicitDefaultSessionId, StringComparison.Ordinal))
                return "implicit_default";
            return "explicit";
        }

        static string SummarizeToolArguments(string internalToolName, Dictionary<string, object>? arguments)
        {
            if (arguments == null || arguments.Count == 0)
                return "none";

            var keys = arguments.Keys.OrderBy(a => a, StringComparer.Ordinal).ToList();
            if (internalToolName == "execute_code")
            {
                var codeLength = OptionalString(arguments, "code")?.Length ?? 0;
                return $"keys=[{string.Join(",", keys)}],code_length={codeLength}";
            }

            return $"keys=[{string.Join(",", keys)}]";
        }
    }
}
