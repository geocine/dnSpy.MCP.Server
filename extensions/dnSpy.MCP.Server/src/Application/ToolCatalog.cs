/*
    Copyright (C) 2026 @geocine

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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using dnSpy.MCP.Server.Contracts;

namespace dnSpy.MCP.Server.Application {
	internal sealed class ToolCatalog {
		readonly List<ToolInfo> tools;

		ToolCatalog(IEnumerable<ToolInfo> tools) {
			this.tools = tools.OrderBy(a => a.Name).ToList();
		}

		public IReadOnlyList<ToolInfo> Tools => tools;

		public static ToolCatalog Create(IEnumerable<ToolInfo> tools) =>
			new ToolCatalog(CreatePublicTools(tools));

		static IEnumerable<ToolInfo> CreatePublicTools(IEnumerable<ToolInfo> tools) {
			var internalTools = tools
				.Select(ToolCatalogMetadata.Enrich)
				.ToList();
			var nameMap = internalTools.ToDictionary(a => a.Name, a => ToolNameMapper.ToPublicName(a.Name));
			return internalTools.Select(a => ToolNameMapper.ToPublicTool(a, nameMap));
		}

		public ToolCatalogStats GetStats() {
			var readOnlyTools = tools.Count(a => a.Annotations?.ReadOnlyHint == true);
			var destructiveTools = tools.Count(a => a.Annotations?.DestructiveHint == true);
			var openWorldTools = tools.Count(a => a.Annotations?.OpenWorldHint == true);
			var toolsWithOutputSchema = tools.Count(a => a.OutputSchema != null);

			return new ToolCatalogStats {
				TotalTools = tools.Count,
				ReadOnlyTools = readOnlyTools,
				MutatingTools = tools.Count - readOnlyTools,
				DestructiveTools = destructiveTools,
				OpenWorldTools = openWorldTools,
				ToolsWithOutputSchema = toolsWithOutputSchema
			};
		}
	}

	internal sealed class ToolCatalogStats {
		public int TotalTools { get; set; }
		public int ReadOnlyTools { get; set; }
		public int MutatingTools { get; set; }
		public int DestructiveTools { get; set; }
		public int OpenWorldTools { get; set; }
		public int ToolsWithOutputSchema { get; set; }
	}

	internal static class ToolNameMapper {
		static readonly Dictionary<string, string> internalToPublicOverrides = new Dictionary<string, string> {
			["find_who_calls_method"] = "dnspy_find_callers",
		};

		static readonly Dictionary<string, string> publicToInternalOverrides = new Dictionary<string, string> {
			["dnspy_status"] = "status",
			["dnspy_get_server_stats"] = "get_server_stats",
			["dnspy_get_logging_status"] = "get_logging_status",
			["dnspy_set_logging"] = "set_logging",
			["dnspy_find_callers"] = "find_who_calls_method",
		};

		public static string ToPublicName(string internalName) {
			if (internalToPublicOverrides.TryGetValue(internalName, out var publicName))
				return publicName;
			return internalName.StartsWith("dnspy_") ? internalName : $"dnspy_{internalName}";
		}

		public static bool TryMapPublicToInternal(string publicName, out string internalName) {
			if (publicToInternalOverrides.TryGetValue(publicName, out var mappedInternalName)) {
				internalName = mappedInternalName;
				return true;
			}

			if (!publicName.StartsWith("dnspy_")) {
				internalName = string.Empty;
				return false;
			}

			internalName = publicName.Substring("dnspy_".Length);
			return true;
		}

		public static ToolInfo ToPublicTool(ToolInfo tool, IReadOnlyDictionary<string, string> publicNameMap) {
			return new ToolInfo {
				Name = ToPublicName(tool.Name),
				Description = RewriteText(tool.Description, publicNameMap),
				InputSchema = RewriteDictionary(tool.InputSchema, publicNameMap),
				OutputSchema = tool.OutputSchema != null ? RewriteDictionary(tool.OutputSchema, publicNameMap) : null,
				Annotations = tool.Annotations
			};
		}

		static Dictionary<string, object> RewriteDictionary(Dictionary<string, object> source, IReadOnlyDictionary<string, string> publicNameMap) {
			var rewritten = new Dictionary<string, object>(source.Count);
			foreach (var kv in source)
				rewritten[kv.Key] = RewriteValue(kv.Value, publicNameMap);
			return rewritten;
		}

		static object RewriteValue(object value, IReadOnlyDictionary<string, string> publicNameMap) {
			if (value is string text)
				return RewriteText(text, publicNameMap);

			if (value is Dictionary<string, object> dict)
				return RewriteDictionary(dict, publicNameMap);

			if (value is List<string> stringList)
				return stringList.Select(a => RewriteText(a, publicNameMap)).ToList();

			if (value is List<object> objectList)
				return objectList.Select(a => RewriteValue(a, publicNameMap)).ToList();

			return value;
		}

		static string RewriteText(string text, IReadOnlyDictionary<string, string> publicNameMap) {
			var rewritten = text;
			foreach (var kv in publicNameMap.OrderByDescending(a => a.Key.Length)) {
				var pattern = $@"(?<!dnspy_)\b{Regex.Escape(kv.Key)}\b";
				rewritten = Regex.Replace(rewritten, pattern, kv.Value);
			}
			return rewritten;
		}
	}

	static class ToolCatalogMetadata {
		static readonly HashSet<string> bootstrapTools = new HashSet<string> {
			"list_tools",
			"status",
			"get_server_stats",
			"get_logging_status",
			"set_logging",
			"get_code_mode_guide",
			"get_code_examples",
			"validate_code_snippet",
			"search_tools",
			"get_tool_schemas",
			"list_tool_groups",
			"enable_tool_groups",
			"disable_tool_groups",
			"get_enabled_tool_groups",
			"execute_code",
		};

		static readonly HashSet<string> mutatingTools = new HashSet<string> {
			"change_member_visibility",
			"rename_member",
			"rename_method",
			"rename_symbol",
			"rename_parameter",
			"save_assembly",
			"edit_assembly_metadata",
			"remove_assembly_attribute",
			"set_assembly_flags",
			"add_assembly_reference",
			"remove_assembly_reference",
			"add_resource",
			"remove_resource",
			"inject_type_from_dll",
			"patch_method_to_ret",
			"load_assembly",
			"select_assembly",
			"close_assembly",
			"close_all_assemblies",
			"write_process_memory",
			"start_debugging",
			"attach_to_process",
			"select_document_node",
			"follow_reference",
			"set_breakpoint",
			"remove_breakpoint",
			"clear_all_breakpoints",
			"continue_debugger",
			"break_debugger",
			"stop_debugging",
			"step_over",
			"step_into",
			"step_out",
			"set_exception_breakpoint",
			"remove_exception_breakpoint",
			"deobfuscate_assembly",
			"save_deobfuscated",
			"run_script",
			"close_dialog",
			"export_to_project",
			"focus_debugger_context",
			"set_logging",
		};

		static readonly HashSet<string> destructiveTools = new HashSet<string> {
			"save_assembly",
			"remove_assembly_attribute",
			"add_assembly_reference",
			"remove_assembly_reference",
			"add_resource",
			"remove_resource",
			"inject_type_from_dll",
			"patch_method_to_ret",
			"close_assembly",
			"close_all_assemblies",
			"write_process_memory",
			"stop_debugging",
			"clear_all_breakpoints",
			"deobfuscate_assembly",
			"save_deobfuscated",
			"run_script",
			"close_dialog"
		};

		static readonly HashSet<string> openWorldTools = new HashSet<string> {
			"load_assembly",
			"save_assembly",
			"get_resource",
			"add_resource",
			"extract_costura",
			"inject_type_from_dll",
			"list_runtime_modules",
			"dump_module_from_memory",
			"read_process_memory",
			"write_process_memory",
			"dump_pe_section",
			"dump_module_unpacked",
			"dump_memory_to_file",
			"unpack_from_memory",
			"start_debugging",
			"attach_to_process",
			"eval_expression",
			"deobfuscate_assembly",
			"save_deobfuscated",
			"run_script",
			"list_dialogs",
			"close_dialog",
			"status",
			"export_to_project",
		};

		static readonly HashSet<string> nonIdempotentTools = new HashSet<string> {
			"rename_symbol",
			"rename_parameter",
			"add_assembly_reference",
			"remove_assembly_reference",
			"add_resource",
			"remove_resource",
			"inject_type_from_dll",
			"patch_method_to_ret",
			"load_assembly",
			"close_assembly",
			"close_all_assemblies",
			"write_process_memory",
			"start_debugging",
			"attach_to_process",
			"select_document_node",
			"follow_reference",
			"set_breakpoint",
			"remove_breakpoint",
			"clear_all_breakpoints",
			"continue_debugger",
			"break_debugger",
			"stop_debugging",
			"step_over",
			"step_into",
			"step_out",
			"set_exception_breakpoint",
			"remove_exception_breakpoint",
			"deobfuscate_assembly",
			"save_deobfuscated",
			"run_script",
			"close_dialog",
			"export_to_project",
			"focus_debugger_context",
			"set_logging",
		};

		public static IReadOnlyList<string> AllGroups { get; } = new[] {
			"bootstrap",
			"reconstruction_core",
			"source_and_decompile",
			"architecture_and_xrefs",
			"metadata_and_native",
			"deobfuscation_and_recovery",
			"provenance_and_correlation",
			"editing_and_patchback",
			"debug_runtime",
			"memory_and_dumping",
			"ui_navigation",
			"scripting_advanced",
			"legacy_compat",
		};

		public static ToolInfo Enrich(ToolInfo tool) {
			return new ToolInfo {
				Name = tool.Name,
				Description = tool.Description,
				InputSchema = tool.InputSchema,
				OutputSchema = tool.OutputSchema,
				Annotations = tool.Annotations ?? CreateAnnotations(tool.Name)
			};
		}

		static ToolAnnotations CreateAnnotations(string toolName) {
			var isMutating = mutatingTools.Contains(toolName);
			return new ToolAnnotations {
				ReadOnlyHint = !isMutating,
				DestructiveHint = destructiveTools.Contains(toolName),
				IdempotentHint = !nonIdempotentTools.Contains(toolName),
				OpenWorldHint = openWorldTools.Contains(toolName)
			};
		}

		public static bool IsBootstrapTool(string toolName) => bootstrapTools.Contains(toolName);

		public static bool IsDiscoverable(string toolName) =>
			toolName != "run_script";

		public static string GetGroup(string toolName) {
			if (bootstrapTools.Contains(toolName))
				return "bootstrap";

			return toolName switch {
				"list_assemblies" or "select_assembly" or "close_assembly" or "close_all_assemblies" or
				"get_assembly_info" or "load_assembly" or "get_member_details" or "normalize_member_id" or
				"resolve_member_id" or "resolve_token" or "get_startup_map" or "get_resource_map" or
				"get_reconstruction_diagnostics" or "get_manifest_and_entrypoints" or "export_to_project" or
				"decompile_baml" or "export_xaml_resources" or "list_resource_elements" or "extract_resx_bundle" =>
					"reconstruction_core",

				"get_decompiled_source" or "batch_get_decompiled_source" or "get_ast_outline" or "decompile_assembly" or
				"decompile_method" or "decompile_type" or "list_types" or "get_type_info" or
				"list_methods_in_type" or "search_methods" or "list_properties_in_type" or
				"get_method_signature" or "get_method_il" or "get_method_il_bytes" or
				"get_method_exception_handlers" or "get_type_fields" or "get_type_property" or
				"find_path_to_type" or "list_events_in_type" or "list_nested_types" =>
					"source_and_decompile",

				"find_who_calls_method" or "find_callees" or "get_method_xrefs" or "find_usages" or
				"find_who_uses_type" or "find_who_reads_field" or "find_who_writes_field" or
				"analyze_type_inheritance" or "find_base_types" or "find_derived_types" or
				"get_implementations" or "get_overrides" or "analyze_call_graph" or
				"find_dependency_chain" or "analyze_cross_assembly_dependencies" or "find_dead_code" or
				"search_members" or "search_methods" or "search_attributes" or "find_string_references" =>
					"architecture_and_xrefs",

				"get_pe_info" or "validate_assembly" or "list_metadata_tables" or "dump_metadata_heap" or
				"list_native_modules" or "get_native_module_map" or "get_native_imports" or
				"get_native_exports" or "get_user_strings" or "scan_pe_strings" or "get_cfg" or
				"get_ssa" or "emulate_method" =>
					"metadata_and_native",

				"list_deobfuscators" or "detect_obfuscator" or "deobfuscate_assembly" or
				"save_deobfuscated" or "analyze_static_constructors" or "find_reflection_usage" or
				"suggest_symbol_renames" or "detect_anti_debug" or "detect_anti_tamper" or
				"find_proxy_methods" or "detect_string_encryption" or "find_delegate_creation" or
				"find_dynamic_code" or "find_byte_arrays" or "find_embedded_pes" or
				"analyze_control_flow" or "get_protection_report" or "triage" or "get_semantic_labels" =>
					"deobfuscation_and_recovery",

				"match_framework_or_package" or "label_third_party_components" or "identify_known_binary" or
				"get_provenance_report" or "load_symbols" or "get_symbol_status" or "find_source_candidates" or
				"match_open_source_candidates" =>
					"provenance_and_correlation",

				"change_member_visibility" or "rename_member" or "rename_method" or "rename_symbol" or
				"rename_parameter" or "save_assembly" or "get_assembly_metadata" or "edit_assembly_metadata" or
				"list_assembly_attributes" or "remove_assembly_attribute" or "set_assembly_flags" or
				"list_assembly_references" or "add_assembly_reference" or "remove_assembly_reference" or
				"list_resources" or "get_resource" or "add_resource" or "remove_resource" or
				"extract_costura" or "inject_type_from_dll" or "list_pinvoke_methods" or
				"patch_method_to_ret" or "get_custom_attributes" =>
					"editing_and_patchback",

				"start_debugging" or "attach_to_process" or "get_debugger_state" or "list_breakpoints" or
				"set_breakpoint" or "set_tracepoint" or "remove_breakpoint" or "clear_all_breakpoints" or
				"continue_debugger" or "break_debugger" or "stop_debugging" or "get_call_stack" or
				"step_over" or "step_into" or "step_out" or "get_current_location" or "wait_for_pause" or
				"inspect_breakpoint" or "set_exception_breakpoint" or "remove_exception_breakpoint" or
				"list_exception_breakpoints" or "get_local_variables" or "eval_expression" =>
					"debug_runtime",

				"list_runtime_modules" or "dump_module_from_memory" or "read_process_memory" or
				"write_process_memory" or "get_pe_sections" or "dump_pe_section" or
				"dump_module_unpacked" or "dump_memory_to_file" or "unpack_from_memory" or
				"dump_cordbg_il" =>
					"memory_and_dumping",

				"get_selected_node" or "get_active_tab" or "select_document_node" or "follow_reference" or
				"get_tool_window_state" or "focus_debugger_context" or "list_dialogs" or "close_dialog" =>
					"ui_navigation",

				"run_script" =>
					"scripting_advanced",

				_ => "legacy_compat"
			};
		}

		public static IReadOnlyList<string> GetTags(string toolName) {
			var tags = new HashSet<string> {
				GetGroup(toolName)
			};

			foreach (var part in toolName.Split('_')) {
				if (!string.IsNullOrWhiteSpace(part))
					tags.Add(part);
			}

			switch (GetGroup(toolName)) {
			case "reconstruction_core":
				tags.Add("reconstruction");
				tags.Add("assembly");
				break;
			case "source_and_decompile":
				tags.Add("source");
				tags.Add("decompile");
				break;
			case "architecture_and_xrefs":
				tags.Add("xref");
				tags.Add("callgraph");
				break;
			case "metadata_and_native":
				tags.Add("metadata");
				tags.Add("native");
				break;
			case "deobfuscation_and_recovery":
				tags.Add("deobfuscation");
				tags.Add("obfuscation");
				break;
			case "provenance_and_correlation":
				tags.Add("framework");
				tags.Add("package");
				tags.Add("known-binary");
				break;
			case "editing_and_patchback":
				tags.Add("rename");
				tags.Add("patchback");
				break;
			case "debug_runtime":
				tags.Add("debugger");
				tags.Add("runtime");
				break;
			case "memory_and_dumping":
				tags.Add("memory");
				tags.Add("dump");
				break;
			case "ui_navigation":
				tags.Add("navigation");
				tags.Add("document");
				break;
			case "scripting_advanced":
				tags.Add("script");
				tags.Add("advanced");
				break;
			}

			return tags.OrderBy(a => a).ToList();
		}
	}
}
