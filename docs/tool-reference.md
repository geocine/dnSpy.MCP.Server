# dnSpy MCP Tool Reference

This reference documents the public `dnspy_*` MCP surface exposed by this repository.

- Descriptions are sourced from `src/Application/McpTools.Schemas.cs` and use the public `dnspy_*` tool names.
- Attribution is called out per tool as either `New in v2 / this repository` or `Improved in v2 / this repository`.
- The lean setup guide stays in `README.md`; this file is the exhaustive tool inventory.

## Sections
- **[Bootstrap and discovery](#bootstrap-and-discovery)** (15 tools)
  - [`dnspy_disable_tool_groups`](#dnspy_disable_tool_groups)
  - [`dnspy_enable_tool_groups`](#dnspy_enable_tool_groups)
  - [`dnspy_execute_code`](#dnspy_execute_code)
  - [`dnspy_get_code_examples`](#dnspy_get_code_examples)
  - [`dnspy_get_code_mode_guide`](#dnspy_get_code_mode_guide)
  - [`dnspy_get_enabled_tool_groups`](#dnspy_get_enabled_tool_groups)
  - [`dnspy_get_logging_status`](#dnspy_get_logging_status)
  - [`dnspy_get_server_stats`](#dnspy_get_server_stats)
  - [`dnspy_get_tool_schemas`](#dnspy_get_tool_schemas)
  - [`dnspy_list_tool_groups`](#dnspy_list_tool_groups)
  - [`dnspy_list_tools`](#dnspy_list_tools)
  - [`dnspy_search_tools`](#dnspy_search_tools)
  - [`dnspy_set_logging`](#dnspy_set_logging)
  - [`dnspy_status`](#dnspy_status)
  - [`dnspy_validate_code_snippet`](#dnspy_validate_code_snippet)
- **[Reconstruction core](#reconstruction-core)** (19 tools)
  - [`dnspy_close_all_assemblies`](#dnspy_close_all_assemblies)
  - [`dnspy_close_assembly`](#dnspy_close_assembly)
  - [`dnspy_decompile_baml`](#dnspy_decompile_baml)
  - [`dnspy_export_to_project`](#dnspy_export_to_project)
  - [`dnspy_export_xaml_resources`](#dnspy_export_xaml_resources)
  - [`dnspy_extract_resx_bundle`](#dnspy_extract_resx_bundle)
  - [`dnspy_get_assembly_info`](#dnspy_get_assembly_info)
  - [`dnspy_get_manifest_and_entrypoints`](#dnspy_get_manifest_and_entrypoints)
  - [`dnspy_get_member_details`](#dnspy_get_member_details)
  - [`dnspy_get_reconstruction_diagnostics`](#dnspy_get_reconstruction_diagnostics)
  - [`dnspy_get_resource_map`](#dnspy_get_resource_map)
  - [`dnspy_get_startup_map`](#dnspy_get_startup_map)
  - [`dnspy_list_assemblies`](#dnspy_list_assemblies)
  - [`dnspy_list_resource_elements`](#dnspy_list_resource_elements)
  - [`dnspy_load_assembly`](#dnspy_load_assembly)
  - [`dnspy_normalize_member_id`](#dnspy_normalize_member_id)
  - [`dnspy_resolve_member_id`](#dnspy_resolve_member_id)
  - [`dnspy_resolve_token`](#dnspy_resolve_token)
  - [`dnspy_select_assembly`](#dnspy_select_assembly)
- **[Source and decompile](#source-and-decompile)** (20 tools)
  - [`dnspy_batch_get_decompiled_source`](#dnspy_batch_get_decompiled_source)
  - [`dnspy_decompile_assembly`](#dnspy_decompile_assembly)
  - [`dnspy_decompile_method`](#dnspy_decompile_method)
  - [`dnspy_decompile_type`](#dnspy_decompile_type)
  - [`dnspy_find_path_to_type`](#dnspy_find_path_to_type)
  - [`dnspy_get_ast_outline`](#dnspy_get_ast_outline)
  - [`dnspy_get_decompiled_source`](#dnspy_get_decompiled_source)
  - [`dnspy_get_method_exception_handlers`](#dnspy_get_method_exception_handlers)
  - [`dnspy_get_method_il`](#dnspy_get_method_il)
  - [`dnspy_get_method_il_bytes`](#dnspy_get_method_il_bytes)
  - [`dnspy_get_method_signature`](#dnspy_get_method_signature)
  - [`dnspy_get_type_fields`](#dnspy_get_type_fields)
  - [`dnspy_get_type_info`](#dnspy_get_type_info)
  - [`dnspy_get_type_property`](#dnspy_get_type_property)
  - [`dnspy_list_events_in_type`](#dnspy_list_events_in_type)
  - [`dnspy_list_methods_in_type`](#dnspy_list_methods_in_type)
  - [`dnspy_list_nested_types`](#dnspy_list_nested_types)
  - [`dnspy_list_properties_in_type`](#dnspy_list_properties_in_type)
  - [`dnspy_list_types`](#dnspy_list_types)
  - [`dnspy_search_methods`](#dnspy_search_methods)
- **[Architecture and xrefs](#architecture-and-xrefs)** (19 tools)
  - [`dnspy_analyze_call_graph`](#dnspy_analyze_call_graph)
  - [`dnspy_analyze_cross_assembly_dependencies`](#dnspy_analyze_cross_assembly_dependencies)
  - [`dnspy_analyze_type_inheritance`](#dnspy_analyze_type_inheritance)
  - [`dnspy_find_base_types`](#dnspy_find_base_types)
  - [`dnspy_find_callees`](#dnspy_find_callees)
  - [`dnspy_find_callers`](#dnspy_find_callers)
  - [`dnspy_find_dead_code`](#dnspy_find_dead_code)
  - [`dnspy_find_dependency_chain`](#dnspy_find_dependency_chain)
  - [`dnspy_find_derived_types`](#dnspy_find_derived_types)
  - [`dnspy_find_string_references`](#dnspy_find_string_references)
  - [`dnspy_find_usages`](#dnspy_find_usages)
  - [`dnspy_find_who_reads_field`](#dnspy_find_who_reads_field)
  - [`dnspy_find_who_uses_type`](#dnspy_find_who_uses_type)
  - [`dnspy_find_who_writes_field`](#dnspy_find_who_writes_field)
  - [`dnspy_get_implementations`](#dnspy_get_implementations)
  - [`dnspy_get_method_xrefs`](#dnspy_get_method_xrefs)
  - [`dnspy_get_overrides`](#dnspy_get_overrides)
  - [`dnspy_search_attributes`](#dnspy_search_attributes)
  - [`dnspy_search_members`](#dnspy_search_members)
- **[Metadata and native analysis](#metadata-and-native-analysis)** (13 tools)
  - [`dnspy_dump_metadata_heap`](#dnspy_dump_metadata_heap)
  - [`dnspy_emulate_method`](#dnspy_emulate_method)
  - [`dnspy_get_cfg`](#dnspy_get_cfg)
  - [`dnspy_get_native_exports`](#dnspy_get_native_exports)
  - [`dnspy_get_native_imports`](#dnspy_get_native_imports)
  - [`dnspy_get_native_module_map`](#dnspy_get_native_module_map)
  - [`dnspy_get_pe_info`](#dnspy_get_pe_info)
  - [`dnspy_get_ssa`](#dnspy_get_ssa)
  - [`dnspy_get_user_strings`](#dnspy_get_user_strings)
  - [`dnspy_list_metadata_tables`](#dnspy_list_metadata_tables)
  - [`dnspy_list_native_modules`](#dnspy_list_native_modules)
  - [`dnspy_scan_pe_strings`](#dnspy_scan_pe_strings)
  - [`dnspy_validate_assembly`](#dnspy_validate_assembly)
- **[Deobfuscation and recovery](#deobfuscation-and-recovery)** (20 tools)
  - [`dnspy_analyze_control_flow`](#dnspy_analyze_control_flow)
  - [`dnspy_analyze_static_constructors`](#dnspy_analyze_static_constructors)
  - [`dnspy_deobfuscate_assembly`](#dnspy_deobfuscate_assembly)
  - [`dnspy_detect_anti_debug`](#dnspy_detect_anti_debug)
  - [`dnspy_detect_anti_tamper`](#dnspy_detect_anti_tamper)
  - [`dnspy_detect_obfuscator`](#dnspy_detect_obfuscator)
  - [`dnspy_detect_string_encryption`](#dnspy_detect_string_encryption)
  - [`dnspy_find_byte_arrays`](#dnspy_find_byte_arrays)
  - [`dnspy_find_delegate_creation`](#dnspy_find_delegate_creation)
  - [`dnspy_find_dynamic_code`](#dnspy_find_dynamic_code)
  - [`dnspy_find_embedded_pes`](#dnspy_find_embedded_pes)
  - [`dnspy_find_proxy_methods`](#dnspy_find_proxy_methods)
  - [`dnspy_find_reflection_usage`](#dnspy_find_reflection_usage)
  - [`dnspy_get_protection_report`](#dnspy_get_protection_report)
  - [`dnspy_get_semantic_labels`](#dnspy_get_semantic_labels)
  - [`dnspy_list_deobfuscators`](#dnspy_list_deobfuscators)
  - [`dnspy_run_de4dot`](#dnspy_run_de4dot)
  - [`dnspy_save_deobfuscated`](#dnspy_save_deobfuscated)
  - [`dnspy_suggest_symbol_renames`](#dnspy_suggest_symbol_renames)
  - [`dnspy_triage`](#dnspy_triage)
- **[Provenance and correlation](#provenance-and-correlation)** (8 tools)
  - [`dnspy_find_source_candidates`](#dnspy_find_source_candidates)
  - [`dnspy_get_provenance_report`](#dnspy_get_provenance_report)
  - [`dnspy_get_symbol_status`](#dnspy_get_symbol_status)
  - [`dnspy_identify_known_binary`](#dnspy_identify_known_binary)
  - [`dnspy_label_third_party_components`](#dnspy_label_third_party_components)
  - [`dnspy_load_symbols`](#dnspy_load_symbols)
  - [`dnspy_match_framework_or_package`](#dnspy_match_framework_or_package)
  - [`dnspy_match_open_source_candidates`](#dnspy_match_open_source_candidates)
- **[Editing and patchback](#editing-and-patchback)** (23 tools)
  - [`dnspy_add_assembly_reference`](#dnspy_add_assembly_reference)
  - [`dnspy_add_resource`](#dnspy_add_resource)
  - [`dnspy_change_member_visibility`](#dnspy_change_member_visibility)
  - [`dnspy_edit_assembly_metadata`](#dnspy_edit_assembly_metadata)
  - [`dnspy_extract_costura`](#dnspy_extract_costura)
  - [`dnspy_get_assembly_metadata`](#dnspy_get_assembly_metadata)
  - [`dnspy_get_custom_attributes`](#dnspy_get_custom_attributes)
  - [`dnspy_get_resource`](#dnspy_get_resource)
  - [`dnspy_inject_type_from_dll`](#dnspy_inject_type_from_dll)
  - [`dnspy_list_assembly_attributes`](#dnspy_list_assembly_attributes)
  - [`dnspy_list_assembly_references`](#dnspy_list_assembly_references)
  - [`dnspy_list_pinvoke_methods`](#dnspy_list_pinvoke_methods)
  - [`dnspy_list_resources`](#dnspy_list_resources)
  - [`dnspy_patch_method_to_ret`](#dnspy_patch_method_to_ret)
  - [`dnspy_remove_assembly_attribute`](#dnspy_remove_assembly_attribute)
  - [`dnspy_remove_assembly_reference`](#dnspy_remove_assembly_reference)
  - [`dnspy_remove_resource`](#dnspy_remove_resource)
  - [`dnspy_rename_member`](#dnspy_rename_member)
  - [`dnspy_rename_method`](#dnspy_rename_method)
  - [`dnspy_rename_parameter`](#dnspy_rename_parameter)
  - [`dnspy_rename_symbol`](#dnspy_rename_symbol)
  - [`dnspy_save_assembly`](#dnspy_save_assembly)
  - [`dnspy_set_assembly_flags`](#dnspy_set_assembly_flags)
- **[Debug runtime](#debug-runtime)** (23 tools)
  - [`dnspy_attach_to_process`](#dnspy_attach_to_process)
  - [`dnspy_break_debugger`](#dnspy_break_debugger)
  - [`dnspy_clear_all_breakpoints`](#dnspy_clear_all_breakpoints)
  - [`dnspy_continue_debugger`](#dnspy_continue_debugger)
  - [`dnspy_eval_expression`](#dnspy_eval_expression)
  - [`dnspy_get_call_stack`](#dnspy_get_call_stack)
  - [`dnspy_get_current_location`](#dnspy_get_current_location)
  - [`dnspy_get_debugger_state`](#dnspy_get_debugger_state)
  - [`dnspy_get_local_variables`](#dnspy_get_local_variables)
  - [`dnspy_inspect_breakpoint`](#dnspy_inspect_breakpoint)
  - [`dnspy_list_breakpoints`](#dnspy_list_breakpoints)
  - [`dnspy_list_exception_breakpoints`](#dnspy_list_exception_breakpoints)
  - [`dnspy_remove_breakpoint`](#dnspy_remove_breakpoint)
  - [`dnspy_remove_exception_breakpoint`](#dnspy_remove_exception_breakpoint)
  - [`dnspy_set_breakpoint`](#dnspy_set_breakpoint)
  - [`dnspy_set_exception_breakpoint`](#dnspy_set_exception_breakpoint)
  - [`dnspy_set_tracepoint`](#dnspy_set_tracepoint)
  - [`dnspy_start_debugging`](#dnspy_start_debugging)
  - [`dnspy_step_into`](#dnspy_step_into)
  - [`dnspy_step_out`](#dnspy_step_out)
  - [`dnspy_step_over`](#dnspy_step_over)
  - [`dnspy_stop_debugging`](#dnspy_stop_debugging)
  - [`dnspy_wait_for_pause`](#dnspy_wait_for_pause)
- **[Memory and dumping](#memory-and-dumping)** (10 tools)
  - [`dnspy_dump_cordbg_il`](#dnspy_dump_cordbg_il)
  - [`dnspy_dump_memory_to_file`](#dnspy_dump_memory_to_file)
  - [`dnspy_dump_module_from_memory`](#dnspy_dump_module_from_memory)
  - [`dnspy_dump_module_unpacked`](#dnspy_dump_module_unpacked)
  - [`dnspy_dump_pe_section`](#dnspy_dump_pe_section)
  - [`dnspy_get_pe_sections`](#dnspy_get_pe_sections)
  - [`dnspy_list_runtime_modules`](#dnspy_list_runtime_modules)
  - [`dnspy_read_process_memory`](#dnspy_read_process_memory)
  - [`dnspy_unpack_from_memory`](#dnspy_unpack_from_memory)
  - [`dnspy_write_process_memory`](#dnspy_write_process_memory)
- **[UI navigation](#ui-navigation)** (8 tools)
  - [`dnspy_close_dialog`](#dnspy_close_dialog)
  - [`dnspy_focus_debugger_context`](#dnspy_focus_debugger_context)
  - [`dnspy_follow_reference`](#dnspy_follow_reference)
  - [`dnspy_get_active_tab`](#dnspy_get_active_tab)
  - [`dnspy_get_selected_node`](#dnspy_get_selected_node)
  - [`dnspy_get_tool_window_state`](#dnspy_get_tool_window_state)
  - [`dnspy_list_dialogs`](#dnspy_list_dialogs)
  - [`dnspy_select_document_node`](#dnspy_select_document_node)
- **[Scripting](#scripting)** (1 tools)
  - [`dnspy_run_script`](#dnspy_run_script)
- **[Legacy compatibility](#legacy-compatibility)** (2 tools)
  - [`dnspy_search_string_literals`](#dnspy_search_string_literals)
  - [`dnspy_search_types`](#dnspy_search_types)

## Bootstrap and discovery

### `dnspy_disable_tool_groups`
**Description:** Disable one or more previously enabled tool groups for the current session.

**Attribution:** New in v2 / this repository

### `dnspy_enable_tool_groups`
**Description:** Enable one or more workflow tool groups for the current session so they become visible through tools/list and directly callable through tools/call. Prefer dnspy_execute_code first; use this when you explicitly want direct tool exposure outside code mode.

**Attribution:** New in v2 / this repository

### `dnspy_execute_code`
**Description:** Run constrained C# code mode for multi-step dnSpy workflows. This is the preferred first tool after initialize when the task needs discovery plus several dnSpy operations. Scripts can print output and call other dnSpy tools with await call_tool("dnspy_get_cfg", new { ... }).

**Attribution:** New in v2 / this repository

### `dnspy_get_code_examples`
**Description:** Return curated dnspy_execute_code examples for common reverse-engineering workflows such as file-format tracing, render-pipeline reconstruction, symbol renaming, and runtime inspection.

**Attribution:** New in v2 / this repository

### `dnspy_get_code_mode_guide`
**Description:** Return structured guidance for dnspy_execute_code, the preferred first tool for multi-step dnSpy analysis. Includes helper functions, limits, common workflows, and likely failure patterns.

**Attribution:** New in v2 / this repository

### `dnspy_get_enabled_tool_groups`
**Description:** Return the tool groups currently enabled for this session and the effective visible tool list.

**Attribution:** New in v2 / this repository

### `dnspy_get_logging_status`
**Description:** Return the current logging and tool-execution telemetry settings without exposing unrelated server configuration.

**Attribution:** New in v2 / this repository

### `dnspy_get_server_stats`
**Description:** Return aggregate tool-catalog and service-availability statistics for the dnSpy MCP server.

**Attribution:** New in v2 / this repository

### `dnspy_get_tool_schemas`
**Description:** Fetch schemas and detailed metadata for specific tools, even when they are not currently enabled for direct invocation.

**Attribution:** New in v2 / this repository

### `dnspy_list_tool_groups`
**Description:** List stable dnSpy tool groups, their intended workflows, and representative tools. Use this before enabling a whole workflow area for the session.

**Attribution:** New in v2 / this repository

### `dnspy_list_tools`
**Description:** List the MCP tools currently visible to this session. In meta-only mode this returns only bootstrap tools plus any enabled tool groups.

**Attribution:** New in v2 / this repository

### `dnspy_search_tools`
**Description:** Search the hidden full dnSpy capability catalog without enabling every tool up front. Use this first to discover the right tool group or exact tool for the workflow you need.

**Attribution:** New in v2 / this repository

### `dnspy_set_logging`
**Description:** Update runtime logging and tool-execution telemetry controls. This is intentionally narrower than exposing general server configuration.

**Attribution:** New in v2 / this repository

### `dnspy_status`
**Description:** Return the current dnSpy MCP server dnspy_status and tool-registration summary.

**Attribution:** New in v2 / this repository

### `dnspy_validate_code_snippet`
**Description:** Compile-check a dnspy_execute_code snippet without running it. Returns diagnostics, referenced tool names, and likely fixes for common code-mode failures.

**Attribution:** New in v2 / this repository

## Reconstruction core

### `dnspy_close_all_assemblies`
**Description:** Close all assemblies currently loaded in dnSpy, clearing the document tree.

**Attribution:** New in v2 / this repository

### `dnspy_close_assembly`
**Description:** Close (remove) a specific assembly from dnSpy. If multiple assemblies share the same name, use 'file_path' (from dnspy_list_assemblies) to target a specific one.

**Attribution:** New in v2 / this repository

### `dnspy_decompile_baml`
**Description:** Decompile a BAML resource element from a loaded assembly back into XAML using dnSpy's BAML decompiler extension when available.

**Attribution:** New in v2 / this repository

### `dnspy_export_to_project`
**Description:** Export a loaded assembly into a reconstruction workspace with decompiled source files, a basic SDK-style project file, raw embedded resources, and optionally decompiled XAML from BAML resources.

**Attribution:** New in v2 / this repository

### `dnspy_export_xaml_resources`
**Description:** Export all discovered BAML resource elements from a loaded assembly into decompiled XAML files under the requested output directory.

**Attribution:** New in v2 / this repository

### `dnspy_extract_resx_bundle`
**Description:** Extract embedded .resources blobs into simplified .resx files that are easier to inspect and migrate during reconstruction work.

**Attribution:** New in v2 / this repository

### `dnspy_get_assembly_info`
**Description:** Get detailed information about a specific assembly

**Attribution:** New in v2 / this repository

### `dnspy_get_manifest_and_entrypoints`
**Description:** Recover manifest-module details and likely startup entrypoints for a loaded assembly, including managed entry points, native entry points, module initializers, and startup-like methods.

**Attribution:** New in v2 / this repository

### `dnspy_get_member_details`
**Description:** Get normalized details for a type, method, field, property, or event using either member_id or legacy symbol reference inputs. Returns canonical identity fields to stabilize downstream analysis.

**Attribution:** New in v2 / this repository

### `dnspy_get_reconstruction_diagnostics`
**Description:** Return a reconstruction-oriented summary of a loaded assembly: target framework, entry point, resource density, static constructor count, assembly references, and namespace hotspots that can help locate parsing or rendering subsystems.

**Attribution:** New in v2 / this repository

### `dnspy_get_resource_map`
**Description:** Summarize embedded resources in a loaded assembly and classify likely BAML, XAML, image, data, or embedded-assembly payloads.

**Attribution:** New in v2 / this repository

### `dnspy_get_startup_map`
**Description:** Map the likely startup surface of a loaded assembly: entry point, module initializer methods, static constructors, and likely startup candidates such as Main, OnStartup, and InitializeComponent.

**Attribution:** New in v2 / this repository

### `dnspy_list_assemblies`
**Description:** List all loaded assemblies in dnSpy

**Attribution:** New in v2 / this repository

### `dnspy_list_resource_elements`
**Description:** List individual resource elements inside embedded .resources blobs, including BAML elements, strings, byte arrays, and other built-in resource payloads.

**Attribution:** New in v2 / this repository

### `dnspy_load_assembly`
**Description:** Load a .NET assembly into dnSpy from disk or from a running process. Mode 1: provide 'file_path' (absolute path). Mode 2: provide 'pid' to dump from a running process (requires active debug session).

**Attribution:** New in v2 / this repository

### `dnspy_normalize_member_id`
**Description:** Resolve a symbol reference or metadata token to the canonical v2 member_id format {module_mvid_n32}:{metadata_token_hex8}:{kind}. Accepts member-style inputs such as assembly_name + type_full_name + method_name.

**Attribution:** New in v2 / this repository

### `dnspy_resolve_member_id`
**Description:** Resolve a canonical member_id back to the corresponding loaded type, method, field, property, or event.

**Attribution:** New in v2 / this repository

### `dnspy_resolve_token`
**Description:** Resolve a .NET metadata token within a loaded assembly and return the resolved member/type/resource identity. Accepts either a hex token like 0x06000001 or an integer token value.

**Attribution:** New in v2 / this repository

### `dnspy_select_assembly`
**Description:** Select an assembly in the dnSpy document tree view and open it in the active tab. This changes the 'current' assembly context for the decompiler and for all subsequent MCP operations that target the selected assembly. Call this after dnspy_load_assembly to switch focus to the newly loaded file. Use 'file_path' to disambiguate when multiple assemblies share the same short name.

**Attribution:** New in v2 / this repository

## Source and decompile

### `dnspy_batch_get_decompiled_source`
**Description:** Decompile multiple members in one call using canonical member_ids. Useful when reconstructing a parser, renderer, and writer pipeline together.

**Attribution:** Improved in v2 / this repository

### `dnspy_decompile_assembly`
**Description:** Decompile a loaded assembly type-by-type with cursor pagination. Useful for whole-assembly reconstruction or focused namespace export without producing one giant response.

**Attribution:** Improved in v2 / this repository

### `dnspy_decompile_method`
**Description:** Decompile a specific method to C# code. Preferred over dnspy_decompile_type for large types (avoids OOM). Use file_path to disambiguate when multiple assemblies share the same name.

**Attribution:** Improved in v2 / this repository

### `dnspy_decompile_type`
**Description:** Decompile an entire type (class/struct/interface/enum) to C# source code. Use file_path to disambiguate when multiple assemblies share the same name.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_path_to_type`
**Description:** Find property/field reference paths from one type to another via BFS traversal of the object graph.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_ast_outline`
**Description:** Parse decompiled or inline C# source with Roslyn and return a structural AST outline. Useful for understanding large decompiled types, method bodies, and obfuscated code flow without reading raw source line-by-line.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_decompiled_source`
**Description:** Decompile a type or method to source using either member_id or legacy symbol reference inputs. For fields, properties, and events, decompiles the declaring type and reports decompile_scope=declaring_type.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_method_exception_handlers`
**Description:** Get exception handlers (try-catch-finally) of a method

**Attribution:** Improved in v2 / this repository

### `dnspy_get_method_il`
**Description:** Get IL instructions of a method

**Attribution:** Improved in v2 / this repository

### `dnspy_get_method_il_bytes`
**Description:** Get raw IL bytes of a method

**Attribution:** Improved in v2 / this repository

### `dnspy_get_method_signature`
**Description:** Get detailed method signature

**Attribution:** Improved in v2 / this repository

### `dnspy_get_type_fields`
**Description:** List fields in a type matching a glob/regex pattern. Supports * and ? wildcards.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_type_info`
**Description:** Get detailed information about a specific type

**Attribution:** Improved in v2 / this repository

### `dnspy_get_type_property`
**Description:** Get detailed information about a single property, including getter/setter info and custom attributes.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_events_in_type`
**Description:** List all events defined in a type

**Attribution:** Improved in v2 / this repository

### `dnspy_list_methods_in_type`
**Description:** List methods in a type. Filter by visibility and/or name pattern (glob or regex).

**Attribution:** Improved in v2 / this repository

### `dnspy_list_nested_types`
**Description:** List all nested types inside a type, recursively

**Attribution:** Improved in v2 / this repository

### `dnspy_list_properties_in_type`
**Description:** List all properties in a type

**Attribution:** Improved in v2 / this repository

### `dnspy_list_types`
**Description:** List types in an assembly or namespace, including nested types. Supports glob (System.* or *Controller) and regex (^System\\..*Controller$) via name_pattern.

**Attribution:** Improved in v2 / this repository

### `dnspy_search_methods`
**Description:** Search for methods across all loaded assemblies, including methods declared on nested and compiler-generated types. Matches against method name, full signature, and declaring type.

**Attribution:** Improved in v2 / this repository

## Architecture and xrefs

### `dnspy_analyze_call_graph`
**Description:** Build a recursive call graph for a method, showing all methods it calls down to a configurable depth. Useful for understanding execution flow.

**Attribution:** Improved in v2 / this repository

### `dnspy_analyze_cross_assembly_dependencies`
**Description:** Compute a dependency matrix for all loaded assemblies, showing which assemblies each assembly depends on (via type references).

**Attribution:** Improved in v2 / this repository

### `dnspy_analyze_type_inheritance`
**Description:** Analyze complete inheritance chain of a type

**Attribution:** Improved in v2 / this repository

### `dnspy_find_base_types`
**Description:** Walk the base-type chain for a target type.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_callees`
**Description:** Find methods directly called by a target method. Useful for following a custom file format reader into decoding, object construction, and render or pack stages.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_callers`
**Description:** Find all methods that call a specific method

**Attribution:** Improved in v2 / this repository

### `dnspy_find_dead_code`
**Description:** Identify methods and types in an assembly that are never called or referenced (static analysis approximation; virtual dispatch and reflection are not tracked).

**Attribution:** Improved in v2 / this repository

### `dnspy_find_dependency_chain`
**Description:** Find all dependency paths (via base types, interfaces, fields, parameters, return types) between two types using BFS traversal.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_derived_types`
**Description:** Find loaded types that derive from a target type.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_string_references`
**Description:** Find exact references to a specific managed string literal and return the methods that load it.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_usages`
**Description:** Find usages of a method, field, or type using a single entry point. Methods return callers, fields return reads and writes, and types return structural usages such as base types, interfaces, field types, return types, and parameter types.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_who_reads_field`
**Description:** Find all methods that read a specific field via IL LDFLD/LDSFLD instructions. Searches across all loaded assemblies.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_who_uses_type`
**Description:** Find all types, methods, and fields that reference a specific type (as base class, interface, field type, parameter, or return type). Searches across all loaded assemblies.

**Attribution:** Improved in v2 / this repository

### `dnspy_find_who_writes_field`
**Description:** Find all methods that write to a specific field via IL STFLD/STSFLD instructions. Searches across all loaded assemblies.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_implementations`
**Description:** Find implementations of a target interface or derived implementations of a base type.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_method_xrefs`
**Description:** Return both callers and callees for a method to quickly place it inside the surrounding control-flow graph.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_overrides`
**Description:** Find overriding methods in derived types that match a target method by name and signature.

**Attribution:** Improved in v2 / this repository

### `dnspy_search_attributes`
**Description:** Search custom attributes across assemblies, types, methods, fields, properties, and events.

**Attribution:** Improved in v2 / this repository

### `dnspy_search_members`
**Description:** Search loaded assemblies for member names across types, methods, fields, properties, and events. Useful for finding candidate parser, serializer, renderer, and packer code even when names are only partially known.

**Attribution:** Improved in v2 / this repository

## Metadata and native analysis

### `dnspy_dump_metadata_heap`
**Description:** Dump entries from a metadata heap in a loaded assembly. Supports strings, blob, guid, and userstrings heaps, with optional direct lookup by heap offset.

**Attribution:** New in v2 / this repository

### `dnspy_emulate_method`
**Description:** Execute a lightweight IL emulation trace for a method, folding simple constants, tracking locals, and stopping cleanly when control flow or external effects become ambiguous.

**Attribution:** New in v2 / this repository

### `dnspy_get_cfg`
**Description:** Build a method-level control-flow graph with basic blocks and edges. Useful for reconstructing parser, decoder, dispatcher, and render flows even when names are obfuscated.

**Attribution:** New in v2 / this repository

### `dnspy_get_native_exports`
**Description:** Parse the PE export directory of an on-disk binary and return named exports with ordinals and RVAs. Useful for mixed-mode modules, native helper DLLs, and reconstruction of unmanaged dependencies.

**Attribution:** New in v2 / this repository

### `dnspy_get_native_imports`
**Description:** List native imports for a managed assembly by inspecting P/Invoke declarations and their resolved DLL/function names. Useful for identifying external decoders, render backends, compression libraries, and anti-debug dependencies.

**Attribution:** New in v2 / this repository

### `dnspy_get_native_module_map`
**Description:** Compose a native-facing view of a loaded assembly: managed P/Invoke imports, embedded PE-like resources, native entry points, and on-disk exports when available.

**Attribution:** New in v2 / this repository

### `dnspy_get_pe_info`
**Description:** Get PE and CLR metadata for a loaded assembly, including machine type, runtime version, entry point, target framework, and module MVID. Prefer file_path when duplicate assembly names are loaded.

**Attribution:** New in v2 / this repository

### `dnspy_get_ssa`
**Description:** Return a lightweight SSA-like view of a method body, including temporary values, local versions, and simplified expressions. Useful when obfuscated names hide real data flow.

**Attribution:** New in v2 / this repository

### `dnspy_get_user_strings`
**Description:** List distinct managed string literals used by the loaded code, grouped with occurrence counts and sample member_ids.

**Attribution:** New in v2 / this repository

### `dnspy_list_metadata_tables`
**Description:** List ECMA-335 metadata tables for a loaded assembly with row counts, row sizes, and file offsets. Useful when reconstructing damaged binaries or understanding metadata shape.

**Attribution:** New in v2 / this repository

### `dnspy_list_native_modules`
**Description:** List all native DLLs imported via P/Invoke (DllImport) in an assembly, grouped by DLL name.

**Attribution:** New in v2 / this repository

### `dnspy_scan_pe_strings`
**Description:** Scan the raw PE file bytes for printable ASCII and UTF-16 strings. Useful for finding URLs, API keys, IP addresses, file paths, and other plaintext data embedded in obfuscated or packed assemblies. 

**Attribution:** New in v2 / this repository

### `dnspy_validate_assembly`
**Description:** Run metadata integrity checks against a loaded assembly and report pass/fail dnspy_status, warnings, and unreadable metadata rows. Useful before export, deeper deobfuscation, or patchback work.

**Attribution:** New in v2 / this repository

## Deobfuscation and recovery

### `dnspy_analyze_control_flow`
**Description:** Return a control-flow summary for methods, including branch counts, switch counts, exception handlers, and simple flattening-style heuristics.

**Attribution:** New in v2 / this repository

### `dnspy_analyze_static_constructors`
**Description:** Enumerate type static constructors (.cctor) and report their called methods, string literals, and decompiled source. Useful for startup-flow recovery, anti-tamper dnspy_triage, and understanding initialization-heavy code.

**Attribution:** New in v2 / this repository

### `dnspy_deobfuscate_assembly`
**Description:** Deobfuscate a .NET assembly using de4dot. Renames mangled symbols, deobfuscates control flow, and decrypts strings. Output is saved to disk.

**Attribution:** New in v2 / this repository

### `dnspy_detect_anti_debug`
**Description:** Detect likely anti-debug routines using de4dot-inspired heuristics such as debugger APIs, profiler-environment strings, debugger-detection literals, and related P/Invoke imports.

**Attribution:** New in v2 / this repository

### `dnspy_detect_anti_tamper`
**Description:** Detect likely self-integrity or anti-tamper routines using de4dot-inspired heuristics such as reading the current assembly, hashing bytes, and failing fast on mismatch.

**Attribution:** New in v2 / this repository

### `dnspy_detect_obfuscator`
**Description:** Detect which obfuscator was applied to a .NET assembly file on disk. Uses de4dot's heuristic detection engine.

**Attribution:** New in v2 / this repository

### `dnspy_detect_string_encryption`
**Description:** Detect likely string-decryptor methods using de4dot-inspired heuristics such as base64 decoding, text-encoding APIs, XOR operations, cryptography calls, and high fan-in from obfuscated callsites.

**Attribution:** New in v2 / this repository

### `dnspy_find_byte_arrays`
**Description:** Find byte-array-heavy fields and methods, including byte[] fields and methods that allocate byte arrays. Useful for payload, archive, and decoder dnspy_triage.

**Attribution:** New in v2 / this repository

### `dnspy_find_delegate_creation`
**Description:** Find methods that create delegates via ldftn, Delegate.CreateDelegate, or direct delegate construction.

**Attribution:** New in v2 / this repository

### `dnspy_find_dynamic_code`
**Description:** Find methods that appear to generate or load code dynamically, such as DynamicMethod, ILGenerator.Emit, expression-tree compilation, or Assembly.Load-style APIs.

**Attribution:** New in v2 / this repository

### `dnspy_find_embedded_pes`
**Description:** Find embedded resources that look like DLLs, EXEs, or compressed managed assemblies by name or MZ header.

**Attribution:** New in v2 / this repository

### `dnspy_find_proxy_methods`
**Description:** Find likely proxy or indirection methods such as tiny forwarding wrappers, delegate-creator helpers, or dynamic-method-based dispatch stubs.

**Attribution:** New in v2 / this repository

### `dnspy_find_reflection_usage`
**Description:** Find methods that appear to use reflection-oriented APIs such as System.Reflection members, Type.GetType, Activator.CreateInstance, member lookup, and reflective invocation.

**Attribution:** New in v2 / this repository

### `dnspy_get_protection_report`
**Description:** Compose anti-debug, anti-tamper, proxy-method, string-decryptor, delegate-creation, dynamic-code, and embedded-PE findings into one protection-oriented summary report.

**Attribution:** New in v2 / this repository

### `dnspy_get_semantic_labels`
**Description:** Return semantic labels for a method or type by combining rename-role heuristics with protection, indirection, dynamic-code, and decryptor detectors.

**Attribution:** New in v2 / this repository

### `dnspy_list_deobfuscators`
**Description:** List all obfuscator types supported by the integrated de4dot engine (e.g. ConfuserEx, Dotfuscator, SmartAssembly, etc.).

**Attribution:** New in v2 / this repository

### `dnspy_run_de4dot`
**Description:** Run de4dot.exe as an external process to deobfuscate a .NET assembly. Supports all de4dot features including dynamic string decryption and ConfuserEx method decryption. Works in all builds.

**Attribution:** New in v2 / this repository

### `dnspy_save_deobfuscated`
**Description:** Return a previously deobfuscated file as a Base64-encoded blob. Useful when the output file cannot be accessed directly.

**Attribution:** New in v2 / this repository

### `dnspy_suggest_symbol_renames`
**Description:** Suggest clearer names for a method or type based on called APIs, string literals, and coarse semantic-role heuristics. Intended to drive iterative patchback renaming during analysis.

**Attribution:** New in v2 / this repository

### `dnspy_triage`
**Description:** Prioritize protection- and indirection-related findings across the loaded binary set so an analyst can start with the most suspicious or obstructive routines first.

**Attribution:** New in v2 / this repository

## Provenance and correlation

### `dnspy_find_source_candidates`
**Description:** Surface likely source and symbol provenance candidates using loaded PDB state, sidecar PDB files, repository metadata, informational version hints, and known framework/package matches.

**Attribution:** New in v2 / this repository

### `dnspy_get_provenance_report`
**Description:** Compose a provenance-oriented report that combines known-binary identification, framework/package matches, and third-party component labels for a loaded assembly.

**Attribution:** New in v2 / this repository

### `dnspy_get_symbol_status`
**Description:** Return per-module symbol dnspy_status, including whether symbols are loaded and whether a sidecar PDB exists next to the module on disk.

**Attribution:** New in v2 / this repository

### `dnspy_identify_known_binary`
**Description:** Classify whether a loaded assembly looks like a known framework, engine, or vendor binary using local assembly-identity heuristics such as assembly name and public key token.

**Attribution:** New in v2 / this repository

### `dnspy_label_third_party_components`
**Description:** Label likely third-party components inside a loaded assembly based on assembly references and namespace ownership. Useful for separating engine/framework/modding code from game-specific logic.

**Attribution:** New in v2 / this repository

### `dnspy_load_symbols`
**Description:** Attempt to load PDB symbols for all metadata-backed modules in a loaded assembly and report which modules ended up with symbol state.

**Attribution:** New in v2 / this repository

### `dnspy_match_framework_or_package`
**Description:** Identify likely frameworks, engines, packages, and SDKs used by a loaded assembly using offline assembly-reference and namespace heuristics.

**Attribution:** New in v2 / this repository

### `dnspy_match_open_source_candidates`
**Description:** Return offline open-source repository candidates for a loaded assembly using repository metadata plus known framework/package and assembly-name rules.

**Attribution:** New in v2 / this repository

## Editing and patchback

### `dnspy_add_assembly_reference`
**Description:** Add an assembly reference (AssemblyRef) by loading a DLL from disk. A TypeForwarder is created to anchor the reference so it persists when saved. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_add_resource`
**Description:** Embed a file from disk as a new EmbeddedResource (ManifestResource) in an assembly. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_change_member_visibility`
**Description:** Change the visibility/access modifier of a type or its members (method, field, property, event). Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_edit_assembly_metadata`
**Description:** Edit assembly-level metadata fields: name, version, culture, or hash algorithm. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_extract_costura`
**Description:** Detect and extract assemblies embedded by Costura.Fody. Costura stores them as EmbeddedResources named 'costura.{name}.dll.compressed' (gzip-compressed) or 'costura.{name}.dll' (uncompressed). Also handles .pdb files. Writes each extracted file to the output directory.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_assembly_metadata`
**Description:** Read assembly-level metadata: name, version, culture, public key, flags, hash algorithm, module count, and custom attributes.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_custom_attributes`
**Description:** Get custom attributes on a type or one of its members. Omit member_name to get the type's own attributes.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_resource`
**Description:** Extract an embedded ManifestResource by name. Returns the raw bytes as Base64 (up to 4 MB inline) and optionally saves to disk. Use skip_base64=true when saving large resources to disk.

**Attribution:** Improved in v2 / this repository

### `dnspy_inject_type_from_dll`
**Description:** Deep-clone a type (fields, methods with IL, properties, events) from an external DLL file into the target assembly. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_assembly_attributes`
**Description:** List all custom attributes declared at assembly level ([assembly: ...] in C#). Useful to discover protections like SuppressIldasmAttribute, ObfuscateAssemblyAttribute, etc.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_assembly_references`
**Description:** List all assembly references (AssemblyRef table entries) in the manifest module.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_pinvoke_methods`
**Description:** List all P/Invoke (DllImport) declarations in a type or the entire assembly. Returns managed name, token, DLL name, and native function name, grouped by DLL when scanning the full assembly. Omit type_full_name to scan all types.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_resources`
**Description:** List all ManifestResource entries in an assembly: embedded resources, linked file references, and assembly-linked resources. Flags Costura.Fody-embedded assemblies (resources starting with 'costura.').

**Attribution:** Improved in v2 / this repository

### `dnspy_patch_method_to_ret`
**Description:** Replace a method's IL body with a minimal return stub (nop + ret) to neutralize it. Ideal for disabling anti-debug, anti-tamper, or other unwanted routines. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_remove_assembly_attribute`
**Description:** Remove one or more custom attributes from the assembly manifest ([assembly: ...] in C#). Example: remove SuppressIldasmAttribute to re-enable ildasm on the saved file. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_remove_assembly_reference`
**Description:** Remove an AssemblyRef entry and all associated TypeForwarder (ExportedType) entries that target it. If the reference is still used by TypeRefs in code, a warning is returned — those usages must also be removed before the reference disappears from the saved file. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_remove_resource`
**Description:** Remove a ManifestResource entry from an assembly by name. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_rename_member`
**Description:** Rename a type or one of its members (method, field, property, event). Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_rename_method`
**Description:** Rename a method safely using its declaring type and optional metadata token. Prefer this over dnspy_rename_member for overloaded methods. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_rename_parameter`
**Description:** Rename a method parameter by canonical method member_id or legacy method reference inputs. This patches metadata-backed parameter names, not transient decompiler locals. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_rename_symbol`
**Description:** Rename a type, method, field, property, or event by canonical member_id or legacy symbol reference inputs. Prefer this for incremental reverse-engineering because member_id stays stable even after earlier renames. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

### `dnspy_save_assembly`
**Description:** Save a (possibly modified) assembly to disk. Persists all in-memory changes made by dnspy_rename_member, dnspy_rename_method, dnspy_change_member_visibility, dnspy_edit_assembly_metadata, etc.

**Attribution:** Improved in v2 / this repository

### `dnspy_set_assembly_flags`
**Description:** Set or clear an individual assembly attribute flag. Changes are in-memory until dnspy_save_assembly is called.

**Attribution:** Improved in v2 / this repository

## Debug runtime

### `dnspy_attach_to_process`
**Description:** Attach the dnSpy debugger to a running .NET process by its PID. Queries all installed debug engine providers for compatible CLR runtimes in the target process.

**Attribution:** Improved in v2 / this repository

### `dnspy_break_debugger`
**Description:** Pause all currently running debugged processes

**Attribution:** Improved in v2 / this repository

### `dnspy_clear_all_breakpoints`
**Description:** Remove all visible breakpoints

**Attribution:** Improved in v2 / this repository

### `dnspy_continue_debugger`
**Description:** Resume execution of all paused debugged processes

**Attribution:** Improved in v2 / this repository

### `dnspy_eval_expression`
**Description:** Evaluate a C# expression in the context of the current paused stack frame, equivalent to the Watch window in dnSpy. Returns the value with type information. Supports field/property access, method calls (with func_eval), arithmetic, and casts. Requires the debugger to be paused.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_call_stack`
**Description:** Get the call stack of the current thread when the debugger is paused. Use dnspy_break_debugger to pause first.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_current_location`
**Description:** Return the current execution location (top frame) of the current or first paused thread. Debugger must be paused.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_debugger_state`
**Description:** Get the current debugger state: whether debugging is active, running or paused, and process information

**Attribution:** Improved in v2 / this repository

### `dnspy_get_local_variables`
**Description:** Read local variables and parameters from a paused debug session stack frame. Returns primitive values, strings, and addresses for complex objects. Requires the debugger to be paused at a breakpoint.

**Attribution:** Improved in v2 / this repository

### `dnspy_inspect_breakpoint`
**Description:** Get detailed information about a visible breakpoint, including condition, hit-count, binding dnspy_status, and per-process bound locations. If neither index nor breakpoint_id is provided, inspects the first visible breakpoint.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_breakpoints`
**Description:** List all code breakpoints currently registered in dnSpy

**Attribution:** Improved in v2 / this repository

### `dnspy_list_exception_breakpoints`
**Description:** List all exception breakpoints that currently have StopFirstChance or StopSecondChance enabled.

**Attribution:** Improved in v2 / this repository

### `dnspy_remove_breakpoint`
**Description:** Remove a breakpoint from a specific method and IL offset

**Attribution:** Improved in v2 / this repository

### `dnspy_remove_exception_breakpoint`
**Description:** Remove an exception breakpoint previously set with dnspy_set_exception_breakpoint.

**Attribution:** Improved in v2 / this repository

### `dnspy_set_breakpoint`
**Description:** Set a breakpoint at a method entry point (or specific IL offset). The breakpoint persists across debug sessions. Use file_path to select the right assembly when multiple share the same name.

**Attribution:** Improved in v2 / this repository

### `dnspy_set_exception_breakpoint`
**Description:** Configure the debugger to pause when a specific exception type is thrown. Useful for catching Anti-Tamper crashes (TypeInitializationException), decryption faults, etc. Category defaults to 'DotNet' for all managed exceptions.

**Attribution:** Improved in v2 / this repository

### `dnspy_set_tracepoint`
**Description:** Set a tracepoint at a method entry point (or specific IL offset). The tracepoint logs a message when hit and can optionally continue execution without breaking.

**Attribution:** Improved in v2 / this repository

### `dnspy_start_debugging`
**Description:** Launch an EXE under the dnSpy debugger. By default breaks at the entry point (after the module initializer has run, so ConfuserEx-decrypted method bodies are already in RAM). Use dnspy_get_debugger_state to poll until paused.

**Attribution:** Improved in v2 / this repository

### `dnspy_step_into`
**Description:** Step into the current statement (enters called methods). Debugger must be paused. Waits for the step to complete and returns the new execution location.

**Attribution:** Improved in v2 / this repository

### `dnspy_step_out`
**Description:** Step out of the current method (runs until the caller resumes). Debugger must be paused. Waits for the step to complete and returns the new execution location.

**Attribution:** Improved in v2 / this repository

### `dnspy_step_over`
**Description:** Step over the current statement. Debugger must be paused. Waits for the step to complete (up to timeout_seconds) and returns the new execution location.

**Attribution:** Improved in v2 / this repository

### `dnspy_stop_debugging`
**Description:** Stop all active debug sessions

**Attribution:** Improved in v2 / this repository

### `dnspy_wait_for_pause`
**Description:** Poll until any debugged process becomes paused (e.g. after dnspy_continue_debugger and a breakpoint hits). Returns process info once paused, or throws TimeoutException.

**Attribution:** Improved in v2 / this repository

## Memory and dumping

### `dnspy_dump_cordbg_il`
**Description:** For each MethodDef in the paused module, reads ICorDebugFunction.ILCode.Address and ILCode.Size via the CorDebug API (through reflection). Reports whether IL addresses fall inside the PE image (mapped encrypted stubs) or outside (hook-decrypted CLR-internal buffers). Requires an active paused debug session. Useful for ConfuserEx JIT-hook analysis.

**Attribution:** Improved in v2 / this repository

### `dnspy_dump_memory_to_file`
**Description:** Save a contiguous range of process memory directly to a file. Supports large ranges up to 256 MB. Useful for dumping unpacked payloads or large data buffers. Requires active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_dump_module_from_memory`
**Description:** Dump a loaded .NET module from process memory to a file. Uses IDbgDotNetRuntime for .NET modules (best quality), falling back to raw ReadMemory. Requires paused or active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_dump_module_unpacked`
**Description:** Dump a full module from process memory with memory-to-file layout conversion. Produces a valid PE file suitable for loading in dnSpy/IDA. Handles .NET, native, and mixed-mode modules. Requires active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_dump_pe_section`
**Description:** Extract a specific PE section (e.g. .text, .data, .rsrc) from a module in process memory. Writes to file and/or returns base64-encoded bytes. Requires active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_get_pe_sections`
**Description:** List PE sections (headers) of a module loaded in the debugged process memory. Returns section names, virtual addresses, sizes, and characteristics. Requires active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_list_runtime_modules`
**Description:** List all .NET modules loaded in the currently debugged processes. Requires an active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_read_process_memory`
**Description:** Read raw bytes from a debugged process address and return a formatted hex dump. Requires active debug session.

**Attribution:** Improved in v2 / this repository

### `dnspy_unpack_from_memory`
**Description:** All-in-one unpacker for ConfuserEx and similar packers: launches the EXE under the debugger pausing at EntryPoint (after the module .cctor has decrypted method bodies), dumps the main module with PE-layout fix, and optionally stops the session. The output file contains readable IL and can be deobfuscated with dnspy_deobfuscate_assembly.

**Attribution:** Improved in v2 / this repository

### `dnspy_write_process_memory`
**Description:** Write bytes to a debugged process address (hot-patching / live memory editing). Useful for disabling checks or patching instructions without modifying the binary on disk. Requires an active debug session. Use dnspy_read_process_memory to verify after writing.

**Attribution:** Improved in v2 / this repository

## UI navigation

### `dnspy_close_dialog`
**Description:** Close a dialog/message-box window by clicking a button. 

**Attribution:** New in v2 / this repository

### `dnspy_focus_debugger_context`
**Description:** Show and focus a known dnSpy debugger tool window context such as call_stack, locals, autos, threads, modules, or processes.

**Attribution:** New in v2 / this repository

### `dnspy_follow_reference`
**Description:** Follow a symbol reference through dnSpy's document tab service. By default it follows the currently selected reference in the active code viewer. You can also provide explicit assembly/type/member or metadata token arguments.

**Attribution:** New in v2 / this repository

### `dnspy_get_active_tab`
**Description:** Return information about dnSpy's active document tab, including the tab title, backing nodes, and the currently selected reference in the document viewer when available.

**Attribution:** New in v2 / this repository

### `dnspy_get_selected_node`
**Description:** Return information about the currently selected node in dnSpy's document tree, including symbol identity and tree path.

**Attribution:** New in v2 / this repository

### `dnspy_get_tool_window_state`
**Description:** Return whether known dnSpy debugger tool windows such as call stack, locals, modules, and processes are currently shown in the UI.

**Attribution:** New in v2 / this repository

### `dnspy_list_dialogs`
**Description:** List active dialog/message-box windows in the dnSpy process. 

**Attribution:** New in v2 / this repository

### `dnspy_select_document_node`
**Description:** Select a document-tree node in dnSpy by assembly/type/member identity or metadata token, and optionally open it in a tab. This changes dnSpy's host navigation state directly.

**Attribution:** New in v2 / this repository

## Scripting

### `dnspy_run_script`
**Description:** Execute arbitrary C# code via Roslyn inside dnSpy's process. 

**Attribution:** Improved in v2 / this repository

## Legacy compatibility

### `dnspy_search_string_literals`
**Description:** Search managed string literals loaded by IL ldstr instructions. This is often the fastest way to find file extensions, magic headers, config keys, UI labels, and serializer or renderer code paths.

**Attribution:** Improved in v2 / this repository

### `dnspy_search_types`
**Description:** Search for types by name across all loaded assemblies, including nested and compiler-generated types. Supports glob wildcards (*IService*) and regex (^My\\..*Repository$).

**Attribution:** Improved in v2 / this repository
