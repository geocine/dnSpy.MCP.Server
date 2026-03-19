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

using System.Collections.Generic;
using System.Linq;
using dnSpy.MCP.Server.Contracts;

namespace dnSpy.MCP.Server.Application
{
    public sealed partial class McpTools
    {
        // ── Aggregator ────────────────────────────────────────────────────────────
        List<ToolInfo> BuildLegacyToolList()
        {
            var tools = new List<ToolInfo>();

            if (CanResolve<AssemblyTools>())
                tools.AddRange(GetAssemblyToolSchemas());
            if (CanResolve<TypeTools>()) {
                tools.AddRange(GetTypeToolSchemas());
                tools.AddRange(GetMethodILToolSchemas());
            }
            if (CanResolve<UsageFindingCommandTools>() || CanResolve<CodeAnalysisHelpers>())
                tools.AddRange(GetAnalysisToolSchemas());
            if (CanResolve<EditTools>()) {
                tools.AddRange(GetEditToolSchemas());
                tools.AddRange(GetResourceToolSchemas());
            }
            if (CanResolve<DebugTools>())
                tools.AddRange(GetDebugToolSchemas());
            if (CanResolve<MemoryInspectTools>() || CanResolve<DumpTools>())
                tools.AddRange(GetMemoryToolSchemas());
            if (CanResolve<De4dotTools>())
                tools.AddRange(GetDeobfuscationToolSchemas());
            if (CanResolve<ScriptTools>())
                tools.AddRange(GetScriptingToolSchemas());
            if (CanResolve<WindowTools>())
                tools.AddRange(GetWindowToolSchemas());
            tools.AddRange(GetUtilityToolSchemas());
            return tools.Where(a => IsToolCallable(a.Name)).ToList();
        }

        public List<ToolInfo> GetAvailableTools() =>
            GetAvailableTools(null);

        // ── Assembly tools ────────────────────────────────────────────────────────
        List<ToolInfo> GetAssemblyToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_assemblies",
                Description = "List all loaded assemblies in dnSpy",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_assembly_info",
                Description = "Get detailed information about a specific assembly",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional cursor for pagination"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "get_pe_info",
                Description = "Get PE and CLR metadata for a loaded assembly, including machine type, runtime version, entry point, target framework, and module MVID. Prefer file_path when duplicate assembly names are loaded.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies. Use this to disambiguate duplicate assembly names."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "validate_assembly",
                Description = "Run metadata integrity checks against a loaded assembly and report pass/fail status, warnings, and unreadable metadata rows. Useful before export, deeper deobfuscation, or patchback work.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["level"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Validation level: minimal, production, or strict. Default production."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "list_metadata_tables",
                Description = "List ECMA-335 metadata tables for a loaded assembly with row counts, row sizes, and file offsets. Useful when reconstructing damaged binaries or understanding metadata shape.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["table"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional table-name filter, for example TypeDef, Method, or ManifestResource."
                        },
                        ["include_empty"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "When true, include tables whose row count is zero. Default false."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "dump_metadata_heap",
                Description = "Dump entries from a metadata heap in a loaded assembly. Supports strings, blob, guid, and userstrings heaps, with optional direct lookup by heap offset.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["heap"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Heap name: strings, blob, guid, or userstrings."
                        },
                        ["offset"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional heap-relative offset to fetch one specific entry, for example 0x42."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional pagination cursor when listing all entries in a heap."
                        }
                    },
                    ["required"] = new List<string> { "heap" }
                }
            },
            new ToolInfo {
                Name = "normalize_member_id",
                Description = "Resolve a symbol reference or metadata token to the canonical v2 member_id format {module_mvid_n32}:{metadata_token_hex8}:{kind}. Accepts member-style inputs such as assembly_name + type_full_name + method_name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["token"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional metadata token such as 0x06000001."
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional full type name."
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional method name when resolving a method."
                        },
                        ["method_signature"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact method signature string to disambiguate overloads."
                        },
                        ["field_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional field name when resolving a field."
                        },
                        ["property_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional property name when resolving a property."
                        },
                        ["event_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional event name when resolving an event."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "resolve_member_id",
                Description = "Resolve a canonical member_id back to the corresponding loaded type, method, field, property, or event.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Canonical member_id in the format {module_mvid_n32}:{metadata_token_hex8}:{kind}."
                        }
                    },
                    ["required"] = new List<string> { "member_id" }
                }
            },
            new ToolInfo {
                Name = "resolve_token",
                Description = "Resolve a .NET metadata token within a loaded assembly and return the resolved member/type/resource identity. Accepts either a hex token like 0x06000001 or an integer token value.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies. Use this to disambiguate duplicate assembly names."
                        },
                        ["token"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Metadata token to resolve, for example 0x02000003 or 100663297."
                        }
                    },
                    ["required"] = new List<string> { "token" }
                }
            },
            new ToolInfo {
                Name = "get_member_details",
                Description = "Get normalized details for a type, method, field, property, or event using either member_id or legacy symbol reference inputs. Returns canonical identity fields to stabilize downstream analysis.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Canonical member_id in the format {module_mvid_n32}:{metadata_token_hex8}:{kind}."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path or member_id is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["token"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional metadata token such as 0x06000001."
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional full type name."
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional method name when resolving a method."
                        },
                        ["method_signature"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact method signature string to disambiguate overloads."
                        },
                        ["field_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional field name when resolving a field."
                        },
                        ["property_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional property name when resolving a property."
                        },
                        ["event_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional event name when resolving an event."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_decompiled_source",
                Description = "Decompile a type or method to source using either member_id or legacy symbol reference inputs. For fields, properties, and events, decompiles the declaring type and reports decompile_scope=declaring_type.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Canonical member_id in the format {module_mvid_n32}:{metadata_token_hex8}:{kind}."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path or member_id is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["token"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional metadata token such as 0x06000001."
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional full type name."
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional method name when resolving a method."
                        },
                        ["method_signature"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact method signature string to disambiguate overloads."
                        },
                        ["field_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional field name when resolving a field."
                        },
                        ["property_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional property name when resolving a property."
                        },
                        ["event_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional event name when resolving an event."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "batch_get_decompiled_source",
                Description = "Decompile multiple members in one call using canonical member_ids. Useful when reconstructing a parser, renderer, and writer pipeline together.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_ids"] = new Dictionary<string, object> {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["description"] = "Array of canonical member_ids to decompile."
                        }
                    },
                    ["required"] = new List<string> { "member_ids" }
                }
            },
            new ToolInfo {
                Name = "get_ast_outline",
                Description = "Parse decompiled or inline C# source with Roslyn and return a structural AST outline. Useful for understanding large decompiled types, method bodies, and obfuscated code flow without reading raw source line-by-line.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional canonical member_id to decompile and outline."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional assembly name when resolving a type or method without member_id."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute assembly path to disambiguate duplicate names."
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional full type name when resolving by symbol reference."
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional method name when resolving by symbol reference."
                        },
                        ["method_signature"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact method signature to disambiguate overloads."
                        },
                        ["field_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional field name when outlining a field's declaring type."
                        },
                        ["property_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional property name when outlining a property's declaring type."
                        },
                        ["event_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional event name when outlining an event's declaring type."
                        },
                        ["source"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional inline C# source to parse directly instead of decompiling from a binary."
                        },
                        ["source_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional display name used when source is provided inline."
                        },
                        ["include_invocations"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "When true, include invocation and object-creation counts in executable members."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "decompile_assembly",
                Description = "Decompile a loaded assembly type-by-type with cursor pagination. Useful for whole-assembly reconstruction or focused namespace export without producing one giant response.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["namespace"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact namespace filter."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "export_to_project",
                Description = "Export a loaded assembly into a reconstruction workspace with decompiled source files, a basic SDK-style project file, raw embedded resources, and optionally decompiled XAML from BAML resources.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["output_dir"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Target directory where the reconstructed project workspace will be written." },
                        ["include_resources"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Export raw embedded resources into a resources folder. Default true." },
                        ["include_xaml"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "When true, decompile discovered BAML resource elements into XAML files. Default true." }
                    },
                    ["required"] = new List<string> { "output_dir" }
                }
            },
            new ToolInfo {
                Name = "analyze_static_constructors",
                Description = "Enumerate type static constructors (.cctor) and report their called methods, string literals, and decompiled source. Useful for startup-flow recovery, anti-tamper triage, and understanding initialization-heavy code.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_startup_map",
                Description = "Map the likely startup surface of a loaded assembly: entry point, module initializer methods, static constructors, and likely startup candidates such as Main, OnStartup, and InitializeComponent.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_resource_map",
                Description = "Summarize embedded resources in a loaded assembly and classify likely BAML, XAML, image, data, or embedded-assembly payloads.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "decompile_baml",
                Description = "Decompile a BAML resource element from a loaded assembly back into XAML using dnSpy's BAML decompiler extension when available.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["resource_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional .resources container name, for example MyGame.g.resources." },
                        ["element_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional BAML element name inside the resource set, for example views/mainwindow.baml." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "export_xaml_resources",
                Description = "Export all discovered BAML resource elements from a loaded assembly into decompiled XAML files under the requested output directory.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["output_dir"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Directory that will receive the generated .xaml files." }
                    },
                    ["required"] = new List<string> { "output_dir" }
                }
            },
            new ToolInfo {
                Name = "list_resource_elements",
                Description = "List individual resource elements inside embedded .resources blobs, including BAML elements, strings, byte arrays, and other built-in resource payloads.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["resource_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional embedded resource container name filter." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional pagination cursor." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "extract_resx_bundle",
                Description = "Extract embedded .resources blobs into simplified .resx files that are easier to inspect and migrate during reconstruction work.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["output_dir"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Directory that will receive the generated .resx files." }
                    },
                    ["required"] = new List<string> { "output_dir" }
                }
            },
            new ToolInfo {
                Name = "get_reconstruction_diagnostics",
                Description = "Return a reconstruction-oriented summary of a loaded assembly: target framework, entry point, resource density, static constructor count, assembly references, and namespace hotspots that can help locate parsing or rendering subsystems.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_manifest_and_entrypoints",
                Description = "Recover manifest-module details and likely startup entrypoints for a loaded assembly, including managed entry points, native entry points, module initializers, and startup-like methods.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_native_module_map",
                Description = "Compose a native-facing view of a loaded assembly: managed P/Invoke imports, embedded PE-like resources, native entry points, and on-disk exports when available.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the loaded assembly. Required unless file_path is provided."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "search_members",
                Description = "Search loaded assemblies for member names across types, methods, fields, properties, and events. Useful for finding candidate parser, serializer, renderer, and packer code even when names are only partially known.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query: glob (* and ?) or regex (use ^/$)."
                        },
                        ["kind"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional member kind filter: type, method, field, property, or event."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string> { "query" }
                }
            },
            new ToolInfo {
                Name = "search_string_literals",
                Description = "Search managed string literals loaded by IL ldstr instructions. This is often the fastest way to find file extensions, magic headers, config keys, UI labels, and serializer or renderer code paths.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query: glob (* and ?) or regex (use ^/$)."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string> { "query" }
                }
            },
            new ToolInfo {
                Name = "get_user_strings",
                Description = "List distinct managed string literals used by the loaded code, grouped with occurrence counts and sample member_ids.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_string_references",
                Description = "Find exact references to a specific managed string literal and return the methods that load it.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["string_value"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Exact string literal value to search for."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string> { "string_value" }
                }
            },
            new ToolInfo {
                Name = "find_reflection_usage",
                Description = "Find methods that appear to use reflection-oriented APIs such as System.Reflection members, Type.GetType, Activator.CreateInstance, member lookup, and reflective invocation.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "suggest_symbol_renames",
                Description = "Suggest clearer names for a method or type based on called APIs, string literals, and coarse semantic-role heuristics. Intended to drive iterative patchback renaming during analysis.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Canonical member_id for a method or type."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Loaded assembly name when using legacy reference inputs."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full type name when using legacy reference inputs."
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Method name when using legacy reference inputs."
                        },
                        ["method_signature"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact method signature to disambiguate overloads."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "detect_anti_debug",
                Description = "Detect likely anti-debug routines using de4dot-inspired heuristics such as debugger APIs, profiler-environment strings, debugger-detection literals, and related P/Invoke imports.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "detect_anti_tamper",
                Description = "Detect likely self-integrity or anti-tamper routines using de4dot-inspired heuristics such as reading the current assembly, hashing bytes, and failing fast on mismatch.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_proxy_methods",
                Description = "Find likely proxy or indirection methods such as tiny forwarding wrappers, delegate-creator helpers, or dynamic-method-based dispatch stubs.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "detect_string_encryption",
                Description = "Detect likely string-decryptor methods using de4dot-inspired heuristics such as base64 decoding, text-encoding APIs, XOR operations, cryptography calls, and high fan-in from obfuscated callsites.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_delegate_creation",
                Description = "Find methods that create delegates via ldftn, Delegate.CreateDelegate, or direct delegate construction.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor from previous response." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_dynamic_code",
                Description = "Find methods that appear to generate or load code dynamically, such as DynamicMethod, ILGenerator.Emit, expression-tree compilation, or Assembly.Load-style APIs.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor from previous response." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_byte_arrays",
                Description = "Find byte-array-heavy fields and methods, including byte[] fields and methods that allocate byte arrays. Useful for payload, archive, and decoder triage.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor from previous response." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_embedded_pes",
                Description = "Find embedded resources that look like DLLs, EXEs, or compressed managed assemblies by name or MZ header.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor from previous response." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "analyze_control_flow",
                Description = "Return a control-flow summary for methods, including branch counts, switch counts, exception handlers, and simple flattening-style heuristics.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor from previous response." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_cfg",
                Description = "Build a method-level control-flow graph with basic blocks and edges. Useful for reconstructing parser, decoder, dispatcher, and render flows even when names are obfuscated.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Preferred stable method identifier." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when member_id is not used." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when member_id is not used." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when member_id is not used." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." },
                        ["metadata_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token such as 0x06000001." },
                        ["include_instructions"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Include IL instruction details inside each block." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_ssa",
                Description = "Return a lightweight SSA-like view of a method body, including temporary values, local versions, and simplified expressions. Useful when obfuscated names hide real data flow.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Preferred stable method identifier." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when member_id is not used." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when member_id is not used." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when member_id is not used." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." },
                        ["metadata_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token such as 0x06000001." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "emulate_method",
                Description = "Execute a lightweight IL emulation trace for a method, folding simple constants, tracking locals, and stopping cleanly when control flow or external effects become ambiguous.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Preferred stable method identifier." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when member_id is not used." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when member_id is not used." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when member_id is not used." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." },
                        ["metadata_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token such as 0x06000001." },
                        ["max_steps"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Maximum emulation steps before stopping. Default 256." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_protection_report",
                Description = "Compose anti-debug, anti-tamper, proxy-method, string-decryptor, delegate-creation, dynamic-code, and embedded-PE findings into one protection-oriented summary report.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "triage",
                Description = "Prioritize protection- and indirection-related findings across the loaded binary set so an analyst can start with the most suspicious or obstructive routines first.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly name filter." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_semantic_labels",
                Description = "Return semantic labels for a method or type by combining rename-role heuristics with protection, indirection, dynamic-code, and decryptor detectors.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical member_id for a method or type." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full type name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "match_framework_or_package",
                Description = "Identify likely frameworks, engines, packages, and SDKs used by a loaded assembly using offline assembly-reference and namespace heuristics.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "label_third_party_components",
                Description = "Label likely third-party components inside a loaded assembly based on assembly references and namespace ownership. Useful for separating engine/framework/modding code from game-specific logic.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "identify_known_binary",
                Description = "Classify whether a loaded assembly looks like a known framework, engine, or vendor binary using local assembly-identity heuristics such as assembly name and public key token.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_provenance_report",
                Description = "Compose a provenance-oriented report that combines known-binary identification, framework/package matches, and third-party component labels for a loaded assembly.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "load_symbols",
                Description = "Attempt to load PDB symbols for all metadata-backed modules in a loaded assembly and report which modules ended up with symbol state.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_symbol_status",
                Description = "Return per-module symbol status, including whether symbols are loaded and whether a sidecar PDB exists next to the module on disk.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_source_candidates",
                Description = "Surface likely source and symbol provenance candidates using loaded PDB state, sidecar PDB files, repository metadata, informational version hints, and known framework/package matches.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "match_open_source_candidates",
                Description = "Return offline open-source repository candidates for a loaded assembly using repository metadata plus known framework/package and assembly-name rules.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the loaded assembly. Required unless file_path is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_callees",
                Description = "Find methods directly called by a target method. Useful for following a custom file format reader into decoding, object construction, and render or pack stages.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical method member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_method_xrefs",
                Description = "Return both callers and callees for a method to quickly place it inside the surrounding control-flow graph.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical method member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_base_types",
                Description = "Walk the base-type chain for a target type.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical type member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full type name when using legacy reference inputs." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_derived_types",
                Description = "Find loaded types that derive from a target type.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical type member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full type name when using legacy reference inputs." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_implementations",
                Description = "Find implementations of a target interface or derived implementations of a base type.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical type member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full type name when using legacy reference inputs." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_overrides",
                Description = "Find overriding methods in derived types that match a target method by name and signature.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical method member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_usages",
                Description = "Find usages of a method, field, or type using a single entry point. Methods return callers, fields return reads and writes, and types return structural usages such as base types, interfaces, field types, return types, and parameter types.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical member_id for a type, method, or field." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name when using legacy reference inputs." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature." },
                        ["field_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Field name when using legacy reference inputs." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "search_attributes",
                Description = "Search custom attributes across assemblies, types, methods, fields, properties, and events.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query: glob (* and ?) or regex (use ^/$)."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional loaded assembly name filter."
                        },
                        ["file_path"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional absolute FilePath from dnspy_list_assemblies."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response."
                        }
                    },
                    ["required"] = new List<string> { "query" }
                }
            },
            new ToolInfo {
                Name = "list_types",
                Description = "List types in an assembly or namespace, including nested types. Supports glob (System.* or *Controller) and regex (^System\\..*Controller$) via name_pattern.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["namespace"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact namespace filter"
                        },
                        ["name_pattern"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional name filter: glob (* and ?) or regex (use ^/$). Matches against type short name and full name."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response nextCursor"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "load_assembly",
                Description = "Load a .NET assembly into dnSpy from disk or from a running process. Mode 1: provide 'file_path' (absolute path). Mode 2: provide 'pid' to dump from a running process (requires active debug session).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["file_path"]     = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to a .NET assembly (.dll/.exe) or a saved memory dump on disk" },
                        ["memory_layout"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Set true when file_path points to a raw memory-layout dump (VA instead of file offsets). Default false." },
                        ["pid"]           = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "PID of a running .NET process. Dumps the main module (or the module matching 'module_name') from process memory and loads it." },
                        ["module_name"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Optional module name filter when using 'pid' (e.g. 'MyApp.dll'). Defaults to the first EXE module." },
                        ["process_id"]    = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Alias for 'pid'" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "select_assembly",
                Description = "Select an assembly in the dnSpy document tree view and open it in the active tab. This changes the 'current' assembly context for the decompiler and for all subsequent MCP operations that target the selected assembly. Call this after load_assembly to switch focus to the newly loaded file. Use 'file_path' to disambiguate when multiple assemblies share the same short name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short name of the assembly to select (e.g. 'BigBearTuning_unpacked'). Use list_assemblies to see loaded names." },
                        ["file_path"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional: absolute path of the loaded file (FilePath from list_assemblies). Use this to pick the correct one when multiple assemblies share the same name." }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "close_assembly",
                Description = "Close (remove) a specific assembly from dnSpy. If multiple assemblies share the same name, use 'file_path' (from list_assemblies) to target a specific one.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short name of the assembly to close." },
                        ["file_path"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional: absolute path (FilePath from list_assemblies) to close a specific copy when names collide." }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "close_all_assemblies",
                Description = "Close all assemblies currently loaded in dnSpy, clearing the document tree.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
        };

        // ── Type inspection tools ─────────────────────────────────────────────────
        List<ToolInfo> GetTypeToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "get_type_info",
                Description = "Get detailed information about a specific type",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional cursor for pagination"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "search_types",
                Description = "Search for types by name across all loaded assemblies, including nested and compiler-generated types. Supports glob wildcards (*IService*) and regex (^My\\..*Repository$).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query: plain substring, glob (* and ?), or regex (use ^/$). Matched against FullName."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response nextCursor"
                        }
                    },
                    ["required"] = new List<string> { "query" }
                }
            },
            new ToolInfo {
                Name = "search_methods",
                Description = "Search for methods across all loaded assemblies, including methods declared on nested and compiler-generated types. Matches against method name, full signature, and declaring type.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query: plain substring, glob (* and ?), or regex (use ^/$). Matched against method name, full signature, and declaring type."
                        },
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional exact assembly name filter."
                        },
                        ["type_pattern"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional declaring-type filter: glob (* and ?) or regex (use ^/$). Matches against type short name and full name."
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response nextCursor"
                        }
                    },
                    ["required"] = new List<string> { "query" }
                }
            },
            new ToolInfo {
                Name = "list_methods_in_type",
                Description = "List methods in a type. Filter by visibility and/or name pattern (glob or regex).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["visibility"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional visibility filter: public, private, protected, or internal"
                        },
                        ["name_pattern"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional name filter: glob (* and ?) or regex (use ^/$). E.g. 'Get*', '^On[A-Z]', 'Async$'"
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response nextCursor"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "list_properties_in_type",
                Description = "List all properties in a type",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["cursor"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Pagination cursor from previous response nextCursor"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "get_method_signature",
                Description = "Get detailed method signature",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the method"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "analyze_type_inheritance",
                Description = "Analyze complete inheritance chain of a type",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "get_type_fields",
                Description = "List fields in a type matching a glob/regex pattern. Supports * and ? wildcards.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["pattern"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Field name pattern: glob (* ?) or regex (^/$). Use * to list all." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Pagination cursor" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "pattern" }
                }
            },
            new ToolInfo {
                Name = "get_type_property",
                Description = "Get detailed information about a single property, including getter/setter info and custom attributes.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["property_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the property" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "property_name" }
                }
            },
            new ToolInfo {
                Name = "find_path_to_type",
                Description = "Find property/field reference paths from one type to another via BFS traversal of the object graph.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["from_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the starting type" },
                        ["to_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name (or substring) of the target type" },
                        ["max_depth"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Maximum BFS depth (default 5)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "from_type", "to_type" }
                }
            },
            new ToolInfo {
                Name = "list_native_modules",
                Description = "List all native DLLs imported via P/Invoke (DllImport) in an assembly, grouped by DLL name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "get_native_imports",
                Description = "List native imports for a managed assembly by inspecting P/Invoke declarations and their resolved DLL/function names. Useful for identifying external decoders, render backends, compression libraries, and anti-debug dependencies.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly path; preferred when multiple loaded assemblies share a name." },
                        ["cursor"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Opaque pagination cursor returned by a previous call." }
                    }
                }
            },
            new ToolInfo {
                Name = "get_native_exports",
                Description = "Parse the PE export directory of an on-disk binary and return named exports with ordinals and RVAs. Useful for mixed-mode modules, native helper DLLs, and reconstruction of unmanaged dependencies.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name whose PE file should be inspected." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Explicit PE path. Required when the loaded assembly has no readable on-disk path." }
                    }
                }
            },
            new ToolInfo {
                Name = "list_events_in_type",
                Description = "List all events defined in a type",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "get_custom_attributes",
                Description = "Get custom attributes on a type or one of its members. Omit member_name to get the type's own attributes.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["member_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the member (optional; omit for type-level attributes)" },
                        ["member_kind"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Kind of member: method, field, property, or event (optional; helps disambiguation)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "list_nested_types",
                Description = "List all nested types inside a type, recursively",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the containing type" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
        };

        // ── Method / IL tools ─────────────────────────────────────────────────────
        List<ToolInfo> GetMethodILToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "decompile_method",
                Description = "Decompile a specific method to C# code. Preferred over decompile_type for large types (avoids OOM). Use file_path to disambiguate when multiple assemblies share the same name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the declaring type (e.g. 'AA9A3FB8' or 'MyNamespace.MyClass')" },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the method (e.g. 'Main', '.ctor', or obfuscated names like '3392BA2B')" },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full path of the assembly file (optional; used to disambiguate when multiple assemblies share the same name)" },
                        ["signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full method signature to select a specific overload (optional, e.g. 'System.Void AA9A3FB8::3392BA2B(System.Object,System.Int32)')" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "get_method_il",
                Description = "Get IL instructions of a method",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the method"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "get_method_il_bytes",
                Description = "Get raw IL bytes of a method",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the method"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "get_method_exception_handlers",
                Description = "Get exception handlers (try-catch-finally) of a method",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the method"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "dump_cordbg_il",
                Description = "For each MethodDef in the paused module, reads ICorDebugFunction.ILCode.Address and ILCode.Size via the CorDebug API (through reflection). Reports whether IL addresses fall inside the PE image (mapped encrypted stubs) or outside (hook-decrypted CLR-internal buffers). Requires an active paused debug session. Useful for ConfuserEx JIT-hook analysis.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["module_name"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Module name or filename filter (default: first exe module)" },
                        ["output_path"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Optional path to save full JSON results to disk" },
                        ["max_methods"]   = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max number of MethodDef tokens to scan (default 10000)" },
                        ["include_bytes"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "If true, include base64-encoded IL bytes (from addr-12) for each method (default false)" }
                    },
                    ["required"] = new List<string>()
                }
            },
        };

        // ── Static analysis tools ─────────────────────────────────────────────────
        List<ToolInfo> GetAnalysisToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "find_who_calls_method",
                Description = "Find all methods that call a specific method",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the assembly"
                        },
                        ["type_full_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Full name of the type"
                        },
                        ["method_name"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Name of the method"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "find_who_uses_type",
                Description = "Find all types, methods, and fields that reference a specific type (as base class, interface, field type, parameter, or return type). Searches across all loaded assemblies.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly containing the target type" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type to search for (e.g. MyNamespace.MyClass)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "find_who_reads_field",
                Description = "Find all methods that read a specific field via IL LDFLD/LDSFLD instructions. Searches across all loaded assemblies.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly containing the type that declares the field" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type that declares the field" },
                        ["field_name"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the field" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "field_name" }
                }
            },
            new ToolInfo {
                Name = "find_who_writes_field",
                Description = "Find all methods that write to a specific field via IL STFLD/STSFLD instructions. Searches across all loaded assemblies.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly containing the type that declares the field" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type that declares the field" },
                        ["field_name"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the field" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "field_name" }
                }
            },
            new ToolInfo {
                Name = "analyze_call_graph",
                Description = "Build a recursive call graph for a method, showing all methods it calls down to a configurable depth. Useful for understanding execution flow.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type containing the method" },
                        ["method_name"]    = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the method to analyze" },
                        ["max_depth"]      = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Maximum recursion depth (default 5)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "find_dependency_chain",
                Description = "Find all dependency paths (via base types, interfaces, fields, parameters, return types) between two types using BFS traversal.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to search in" },
                        ["from_type"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the starting type" },
                        ["to_type"]       = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name (or simple name) of the target type" },
                        ["max_length"]    = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Maximum path length (default 10)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "from_type", "to_type" }
                }
            },
            new ToolInfo {
                Name = "analyze_cross_assembly_dependencies",
                Description = "Compute a dependency matrix for all loaded assemblies, showing which assemblies each assembly depends on (via type references).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "find_dead_code",
                Description = "Identify methods and types in an assembly that are never called or referenced (static analysis approximation; virtual dispatch and reflection are not tracked).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to analyze" },
                        ["include_private"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Include private members in dead code detection (default true)" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "scan_pe_strings",
                Description = "Scan the raw PE file bytes for printable ASCII and UTF-16 strings. Useful for finding URLs, API keys, IP addresses, file paths, and other plaintext data embedded in obfuscated or packed assemblies. " +
                              "Use file_path when two assemblies share the same internal name (e.g. packed original + unpacked copy) to avoid scanning the wrong file.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]    = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Name of the loaded assembly to scan. Required unless file_path is provided." },
                        ["file_path"]        = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Direct absolute path to the PE file on disk. Takes priority over assembly_name. Use this when multiple assemblies share the same internal name." },
                        ["min_length"]       = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Minimum string length to include (default 5)" },
                        ["encoding"]         = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Encoding scan mode alias: ascii, unicode, utf16, or both. When omitted, ASCII + UTF-16 are scanned." },
                        ["include_utf16"]    = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Also scan for UTF-16 LE strings (default true)" },
                        ["filter_pattern"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Optional regex to filter results (e.g. 'https?://' to find only URLs)" }
                    },
                    ["required"] = new List<string> { }
                }
            },
        };

        // ── Edit tools ────────────────────────────────────────────────────────────
        List<ToolInfo> GetEditToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "decompile_type",
                Description = "Decompile an entire type (class/struct/interface/enum) to C# source code. Use file_path to disambiguate when multiple assemblies share the same name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type (e.g. MyNamespace.MyClass)" },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full file path to the assembly (optional; used to disambiguate when multiple assemblies share the same name)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name" }
                }
            },
            new ToolInfo {
                Name = "change_member_visibility",
                Description = "Change the visibility/access modifier of a type or its members (method, field, property, event). Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the containing type (or the type itself when member_kind=type)" },
                        ["member_kind"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Kind of member: type, method, field, property, or event" },
                        ["member_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the member (ignored when member_kind=type)" },
                        ["new_visibility"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New visibility: public, private, protected, internal, protected_internal, private_protected" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "member_kind", "new_visibility" }
                }
            },
            new ToolInfo {
                Name = "rename_member",
                Description = "Rename a type or one of its members (method, field, property, event). Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["member_kind"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Kind of member: type, method, field, property, or event" },
                        ["old_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Current name of the member" },
                        ["new_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New name for the member" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "member_kind", "old_name", "new_name" }
                }
            },
            new ToolInfo {
                Name = "rename_method",
                Description = "Rename a method safely using its declaring type and optional metadata token. Prefer this over rename_member for overloaded methods. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the declaring type (supports nested types)" },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Current method name. Required even when method_token is provided." },
                        ["new_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New method name" },
                        ["method_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token to disambiguate overloads, e.g. 0x06001234" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name", "new_name" }
                }
            },
            new ToolInfo {
                Name = "rename_symbol",
                Description = "Rename a type, method, field, property, or event by canonical member_id or legacy symbol reference inputs. Prefer this for incremental reverse-engineering because member_id stays stable even after earlier renames. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical member_id in the format {module_mvid_n32}:{metadata_token_hex8}:{kind}." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name. Required unless member_id is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." },
                        ["field_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Field name when using legacy reference inputs." },
                        ["property_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Property name when using legacy reference inputs." },
                        ["event_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Event name when using legacy reference inputs." },
                        ["member_kind"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Legacy fallback kind: type, method, field, property, or event." },
                        ["member_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Legacy fallback member name used with member_kind." },
                        ["new_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New metadata name to assign." }
                    },
                    ["required"] = new List<string> { "new_name" }
                }
            },
            new ToolInfo {
                Name = "rename_parameter",
                Description = "Rename a method parameter by canonical method member_id or legacy method reference inputs. This patches metadata-backed parameter names, not transient decompiler locals. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["member_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Canonical method member_id." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Loaded assembly name. Required unless member_id is provided." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional absolute FilePath from dnspy_list_assemblies." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Declaring type full name when using legacy reference inputs." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Method name when using legacy reference inputs." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional exact method signature to disambiguate overloads." },
                        ["parameter_index"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Zero-based parameter index among real parameters." },
                        ["old_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Existing parameter name. Use this instead of parameter_index if you prefer name-based targeting." },
                        ["new_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New parameter metadata name to assign." }
                    },
                    ["required"] = new List<string> { "new_name" }
                }
            },
            new ToolInfo {
                Name = "save_assembly",
                Description = "Save a (possibly modified) assembly to disk. Persists all in-memory changes made by rename_member, rename_method, change_member_visibility, edit_assembly_metadata, etc.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to save" },
                        ["output_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Output file path. Defaults to the original file location." }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "get_assembly_metadata",
                Description = "Read assembly-level metadata: name, version, culture, public key, flags, hash algorithm, module count, and custom attributes.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "edit_assembly_metadata",
                Description = "Edit assembly-level metadata fields: name, version, culture, or hash algorithm. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]   = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to edit" },
                        ["new_name"]        = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New assembly name (optional)" },
                        ["version"]         = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New version as major.minor.build.revision (optional, e.g. '2.0.0.0')" },
                        ["culture"]         = new Dictionary<string, object> { ["type"] = "string", ["description"] = "New culture string, e.g. '' (neutral), 'en-US' (optional)" },
                        ["hash_algorithm"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Hash algorithm: SHA1, MD5, or None (optional)" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "list_assembly_attributes",
                Description = "List all custom attributes declared at assembly level ([assembly: ...] in C#). Useful to discover protections like SuppressIldasmAttribute, ObfuscateAssemblyAttribute, etc.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to inspect" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "remove_assembly_attribute",
                Description = "Remove one or more custom attributes from the assembly manifest ([assembly: ...] in C#). Example: remove SuppressIldasmAttribute to re-enable ildasm on the saved file. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]      = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to modify" },
                        ["attribute_type_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Simple or fully-qualified name of the attribute to remove, e.g. 'SuppressIldasmAttribute' or 'System.Runtime.CompilerServices.SuppressIldasmAttribute'" },
                        ["index"]              = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional 0-based index among matching attributes to remove a specific one. Omit to remove ALL matching attributes." }
                    },
                    ["required"] = new List<string> { "assembly_name", "attribute_type_name" }
                }
            },
            new ToolInfo {
                Name = "set_assembly_flags",
                Description = "Set or clear an individual assembly attribute flag. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["flag_name"]     = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Flag to change: PublicKey | Retargetable | DisableJITOptimizer | EnableJITTracking | WindowsRuntime | ProcessorArchitecture"
                        },
                        ["value"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "true/false for boolean flags; architecture name for ProcessorArchitecture (AnyCPU | x86 | AMD64 | ARM | ARM64 | IA64)"
                        }
                    },
                    ["required"] = new List<string> { "assembly_name", "flag_name", "value" }
                }
            },
            new ToolInfo {
                Name = "list_assembly_references",
                Description = "List all assembly references (AssemblyRef table entries) in the manifest module.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "add_assembly_reference",
                Description = "Add an assembly reference (AssemblyRef) by loading a DLL from disk. A TypeForwarder is created to anchor the reference so it persists when saved. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the target assembly to add the reference to" },
                        ["dll_path"]      = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to the DLL to reference" },
                        ["type_name"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Specific public type to use as the TypeForwarder anchor (optional; defaults to first public type)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "dll_path" }
                }
            },
            new ToolInfo {
                Name = "remove_assembly_reference",
                Description = "Remove an AssemblyRef entry and all associated TypeForwarder (ExportedType) entries that target it. If the reference is still used by TypeRefs in code, a warning is returned — those usages must also be removed before the reference disappears from the saved file. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly to modify" },
                        ["reference_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short name of the assembly reference to remove (e.g. System.Drawing)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "reference_name" }
                }
            },
            new ToolInfo {
                Name = "inject_type_from_dll",
                Description = "Deep-clone a type (fields, methods with IL, properties, events) from an external DLL file into the target assembly. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]    = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the target assembly" },
                        ["dll_path"]         = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to the source DLL" },
                        ["source_type"]      = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name (or simple name) of the type to inject" },
                        ["target_namespace"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Namespace for the injected type in the target assembly (optional; defaults to source namespace)" },
                        ["overwrite"]        = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Replace existing type with same name/namespace if present (default false)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "dll_path", "source_type" }
                }
            },
            new ToolInfo {
                Name = "list_pinvoke_methods",
                Description = "List all P/Invoke (DllImport) declarations in a type or the entire assembly. Returns managed name, token, DLL name, and native function name, grouped by DLL when scanning the full assembly. Omit type_full_name to scan all types.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly to inspect" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional: full name of a specific type to inspect. Omit to scan all types in the assembly." }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "patch_method_to_ret",
                Description = "Replace a method's IL body with a minimal return stub (nop + ret) to neutralize it. Ideal for disabling anti-debug, anti-tamper, or other unwanted routines. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]   = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly containing the method" },
                        ["type_full_name"]  = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type containing the method (including namespace)" },
                        ["method_name"]     = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Simple name of the method to patch" },
                        ["method_token"]    = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token (hex like 0x06001234 or decimal) to disambiguate overloaded methods" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
        };

        // ── Resource tools ────────────────────────────────────────────────────────
        List<ToolInfo> GetResourceToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_resources",
                Description = "List all ManifestResource entries in an assembly: embedded resources, linked file references, and assembly-linked resources. Flags Costura.Fody-embedded assemblies (resources starting with 'costura.').",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly to inspect" }
                    },
                    ["required"] = new List<string> { "assembly_name" }
                }
            },
            new ToolInfo {
                Name = "get_resource",
                Description = "Extract an embedded ManifestResource by name. Returns the raw bytes as Base64 (up to 4 MB inline) and optionally saves to disk. Use skip_base64=true when saving large resources to disk.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Assembly containing the resource" },
                        ["resource_name"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Exact resource name (use list_resources to find it)" },
                        ["output_path"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Optional absolute path to save the raw resource bytes to disk" },
                        ["skip_base64"]   = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Omit Base64 payload from the response (default false)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "resource_name" }
                }
            },
            new ToolInfo {
                Name = "add_resource",
                Description = "Embed a file from disk as a new EmbeddedResource (ManifestResource) in an assembly. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Target assembly" },
                        ["resource_name"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Name for the new resource (e.g. MyApp.config or costura.foo.dll.compressed)" },
                        ["file_path"]     = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to the file to embed" },
                        ["is_public"]     = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Resource visibility: true = Public (default), false = Private" }
                    },
                    ["required"] = new List<string> { "assembly_name", "resource_name", "file_path" }
                }
            },
            new ToolInfo {
                Name = "remove_resource",
                Description = "Remove a ManifestResource entry from an assembly by name. Changes are in-memory until save_assembly is called.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly containing the resource" },
                        ["resource_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Exact resource name to remove (use list_resources to find it)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "resource_name" }
                }
            },
            new ToolInfo {
                Name = "extract_costura",
                Description = "Detect and extract assemblies embedded by Costura.Fody. Costura stores them as EmbeddedResources named 'costura.{name}.dll.compressed' (gzip-compressed) or 'costura.{name}.dll' (uncompressed). Also handles .pdb files. Writes each extracted file to the output directory.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"]    = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Assembly that uses Costura.Fody (use list_resources to confirm costura.* resources exist)" },
                        ["output_directory"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Directory where extracted DLLs and PDBs will be written (created if it does not exist)" },
                        ["decompress"]       = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Decompress gzip-compressed resources (default true)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "output_directory" }
                }
            },
        };

        // ── Debugger tools ────────────────────────────────────────────────────────
        List<ToolInfo> GetDebugToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "get_debugger_state",
                Description = "Get the current debugger state: whether debugging is active, running or paused, and process information",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "list_breakpoints",
                Description = "List all code breakpoints currently registered in dnSpy",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "inspect_breakpoint",
                Description = "Get detailed information about a visible breakpoint, including condition, hit-count, binding status, and per-process bound locations. If neither index nor breakpoint_id is provided, inspects the first visible breakpoint.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["index"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Visible breakpoint index from list_breakpoints (optional)" },
                        ["breakpoint_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Breakpoint id from list_breakpoints (optional)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_selected_node",
                Description = "Return information about the currently selected node in dnSpy's document tree, including symbol identity and tree path.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_active_tab",
                Description = "Return information about dnSpy's active document tab, including the tab title, backing nodes, and the currently selected reference in the document viewer when available.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "select_document_node",
                Description = "Select a document-tree node in dnSpy by assembly/type/member identity or metadata token, and optionally open it in a tab. This changes dnSpy's host navigation state directly.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly short name. Required unless file_path is used." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly path used to disambiguate assemblies with the same short name." },
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional module name or file name when selecting a module." },
                        ["metadata_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token such as 0x06000001. When provided, selects the resolved symbol directly." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional full type name to select." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional method name when selecting a method." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional method signature to disambiguate overloads." },
                        ["field_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional field name when selecting a field." },
                        ["property_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional property name when selecting a property." },
                        ["event_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional event name when selecting an event." },
                        ["open_in_tab"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Open the selected symbol in a document tab (default true)." },
                        ["new_tab"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Open the target in a new tab instead of the active tab (default false)." },
                        ["set_focus"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Give focus to the tree/tab after selection (default true)." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "follow_reference",
                Description = "Follow a symbol reference through dnSpy's document tab service. By default it follows the currently selected reference in the active code viewer. You can also provide explicit assembly/type/member or metadata token arguments.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["use_selected_reference"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "If true or omitted, use the active tab's selected reference at the caret." },
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Assembly short name when following an explicit symbol reference." },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional loaded assembly path used to disambiguate explicit symbol references." },
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional module name or file name when following a module reference." },
                        ["metadata_token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional metadata token such as 0x06000001 for direct token-based navigation." },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional full type name for explicit navigation." },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional method name for explicit navigation." },
                        ["method_signature"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional method signature to disambiguate overloads." },
                        ["field_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional field name for explicit navigation." },
                        ["property_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional property name for explicit navigation." },
                        ["event_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional event name for explicit navigation." },
                        ["new_tab"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Open the followed reference in a new tab (default false)." },
                        ["set_focus"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Give focus to the destination tab (default true)." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_tool_window_state",
                Description = "Return whether known dnSpy debugger tool windows such as call stack, locals, modules, and processes are currently shown in the UI.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["context"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional single tool window context to query, such as call_stack, locals, modules, or processes." }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "focus_debugger_context",
                Description = "Show and focus a known dnSpy debugger tool window context such as call_stack, locals, autos, threads, modules, or processes.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["context"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Tool window context to show, such as call_stack, locals, autos, threads, modules, code_breakpoints, or processes." }
                    },
                    ["required"] = new List<string> { "context" }
                }
            },
            new ToolInfo {
                Name = "set_breakpoint",
                Description = "Set a breakpoint at a method entry point (or specific IL offset). The breakpoint persists across debug sessions. Use file_path to select the right assembly when multiple share the same name.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type (supports nested types, e.g. 'AA9A3FB8')" },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the method" },
                        ["il_offset"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "IL offset within the method body (default 0 = method entry)" },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full path to the assembly file (optional; disambiguates when multiple assemblies share the same name)" },
                        ["condition"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional C# condition expression — breakpoint only fires when true (e.g. \"i > 100\", \"value != null\")" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "set_tracepoint",
                Description = "Set a tracepoint at a method entry point (or specific IL offset). The tracepoint logs a message when hit and can optionally continue execution without breaking.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the method" },
                        ["il_offset"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "IL offset within the method body (default 0 = method entry)" },
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full path to the assembly file (optional; disambiguates when multiple assemblies share the same name)" },
                        ["message"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional tracepoint message template." },
                        ["continue_execution"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "If true, continue execution after logging (default true)." }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "remove_breakpoint",
                Description = "Remove a breakpoint from a specific method and IL offset",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["assembly_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the assembly" },
                        ["type_full_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full name of the type" },
                        ["method_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name of the method" },
                        ["il_offset"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "IL offset of the breakpoint (default 0)" }
                    },
                    ["required"] = new List<string> { "assembly_name", "type_full_name", "method_name" }
                }
            },
            new ToolInfo {
                Name = "clear_all_breakpoints",
                Description = "Remove all visible breakpoints",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "continue_debugger",
                Description = "Resume execution of all paused debugged processes",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "break_debugger",
                Description = "Pause all currently running debugged processes",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "stop_debugging",
                Description = "Stop all active debug sessions",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_call_stack",
                Description = "Get the call stack of the current thread when the debugger is paused. Use break_debugger to pause first.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "start_debugging",
                Description = "Launch an EXE under the dnSpy debugger. By default breaks at the entry point (after the module initializer has run, so ConfuserEx-decrypted method bodies are already in RAM). Use get_debugger_state to poll until paused.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["exe_path"]          = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to the .NET Framework EXE to debug" },
                        ["arguments"]         = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Command-line arguments passed to the process (optional)" },
                        ["working_directory"] = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Working directory (default: EXE directory)" },
                        ["break_kind"]        = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Where to break: EntryPoint (default) | ModuleCctorOrEntryPoint | CreateProcess | DontBreak" }
                    },
                    ["required"] = new List<string> { "exe_path" }
                }
            },
            new ToolInfo {
                Name = "attach_to_process",
                Description = "Attach the dnSpy debugger to a running .NET process by its PID. Queries all installed debug engine providers for compatible CLR runtimes in the target process.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Process ID (PID) of the target process" }
                    },
                    ["required"] = new List<string> { "process_id" }
                }
            },
            new ToolInfo {
                Name = "step_over",
                Description = "Step over the current statement. Debugger must be paused. Waits for the step to complete (up to timeout_seconds) and returns the new execution location.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"]      = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: PID to target when multiple processes are debugged" },
                        ["thread_id"]       = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: specific thread ID to step (default: current/first paused thread)" },
                        ["timeout_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max seconds to wait for step completion (default 30)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "step_into",
                Description = "Step into the current statement (enters called methods). Debugger must be paused. Waits for the step to complete and returns the new execution location.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"]      = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: PID to target when multiple processes are debugged" },
                        ["thread_id"]       = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: specific thread ID to step (default: current/first paused thread)" },
                        ["timeout_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max seconds to wait for step completion (default 30)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "step_out",
                Description = "Step out of the current method (runs until the caller resumes). Debugger must be paused. Waits for the step to complete and returns the new execution location.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"]      = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: PID to target when multiple processes are debugged" },
                        ["thread_id"]       = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: specific thread ID to step (default: current/first paused thread)" },
                        ["timeout_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max seconds to wait for step completion (default 30)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_current_location",
                Description = "Return the current execution location (top frame) of the current or first paused thread. Debugger must be paused.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: PID to target when multiple processes are debugged" },
                        ["thread_id"]  = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: specific thread ID (default: current/first paused thread)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "wait_for_pause",
                Description = "Poll until any debugged process becomes paused (e.g. after continue_debugger and a breakpoint hits). Returns process info once paused, or throws TimeoutException.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["timeout_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max seconds to wait for a pause (default 30)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "set_exception_breakpoint",
                Description = "Configure the debugger to pause when a specific exception type is thrown. Useful for catching Anti-Tamper crashes (TypeInitializationException), decryption faults, etc. Category defaults to 'DotNet' for all managed exceptions.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["exception_type"]  = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Full CLR exception type name, e.g. 'System.TypeInitializationException' or 'System.AccessViolationException'" },
                        ["first_chance"]    = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Break on first-chance (before the exception propagates). Default: true" },
                        ["second_chance"]   = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Break on second-chance (unhandled exception). Default: false" },
                        ["category"]        = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Exception category (default 'DotNet'). Use 'MDA' for Managed Debugging Assistants." }
                    },
                    ["required"] = new List<string> { "exception_type" }
                }
            },
            new ToolInfo {
                Name = "remove_exception_breakpoint",
                Description = "Remove an exception breakpoint previously set with set_exception_breakpoint.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["exception_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Full CLR exception type name (must match the name used in set_exception_breakpoint)" },
                        ["category"]       = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Exception category (default 'DotNet')" }
                    },
                    ["required"] = new List<string> { "exception_type" }
                }
            },
            new ToolInfo {
                Name = "list_exception_breakpoints",
                Description = "List all exception breakpoints that currently have StopFirstChance or StopSecondChance enabled.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
        };

        // ── Memory / PE dump tools ────────────────────────────────────────────────
        List<ToolInfo> GetMemoryToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_runtime_modules",
                Description = "List all .NET modules loaded in the currently debugged processes. Requires an active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: filter by process ID" },
                        ["name_filter"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional: filter by module name (glob or regex)" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "dump_module_from_memory",
                Description = "Dump a loaded .NET module from process memory to a file. Uses IDbgDotNetRuntime for .NET modules (best quality), falling back to raw ReadMemory. Requires paused or active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Module name, filename, or basename (e.g. MyApp.dll)" },
                        ["output_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to write the dumped module (e.g. C:\\dump\\MyApp_dumped.dll)" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID when multiple processes are debugged" }
                    },
                    ["required"] = new List<string> { "module_name", "output_path" }
                }
            },
            new ToolInfo {
                Name = "read_process_memory",
                Description = "Read raw bytes from a debugged process address and return a formatted hex dump. Requires active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["address"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Memory address as hex string (0x7FF000) or decimal" },
                        ["size"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Number of bytes to read (1-65536)" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID" }
                    },
                    ["required"] = new List<string> { "address", "size" }
                }
            },
            new ToolInfo {
                Name = "write_process_memory",
                Description = "Write bytes to a debugged process address (hot-patching / live memory editing). Useful for disabling checks or patching instructions without modifying the binary on disk. Requires an active debug session. Use read_process_memory to verify after writing.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["address"]      = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Target address as hex (0x7FF000) or decimal" },
                        ["bytes_base64"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Bytes to write as base64 (use this or hex_bytes)" },
                        ["hex_bytes"]    = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Bytes to write as hex string, e.g. \"90 90 FF\" or \"9090FF\" (use this or bytes_base64)" },
                        ["process_id"]   = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID when multiple processes are being debugged" }
                    },
                    ["required"] = new List<string> { "address" }
                }
            },
            new ToolInfo {
                Name = "get_pe_sections",
                Description = "List PE sections (headers) of a module loaded in the debugged process memory. Returns section names, virtual addresses, sizes, and characteristics. Requires active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Module name, filename, or basename (e.g. MyApp.dll)" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID" }
                    },
                    ["required"] = new List<string> { "module_name" }
                }
            },
            new ToolInfo {
                Name = "dump_pe_section",
                Description = "Extract a specific PE section (e.g. .text, .data, .rsrc) from a module in process memory. Writes to file and/or returns base64-encoded bytes. Requires active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Module name, filename, or basename (e.g. MyApp.dll)" },
                        ["section_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "PE section name, e.g. .text, .data, .rsrc, .rdata" },
                        ["output_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional: absolute path to write the section bytes to disk" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID" }
                    },
                    ["required"] = new List<string> { "module_name", "section_name" }
                }
            },
            new ToolInfo {
                Name = "dump_module_unpacked",
                Description = "Dump a full module from process memory with memory-to-file layout conversion. Produces a valid PE file suitable for loading in dnSpy/IDA. Handles .NET, native, and mixed-mode modules. Requires active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["module_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Module name, filename, or basename (e.g. MyApp.dll)" },
                        ["output_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to write the dumped PE file" },
                        ["try_fix_pe_layout"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Convert memory layout to file layout (section VA→PointerToRawData remapping). Default true." },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID" }
                    },
                    ["required"] = new List<string> { "module_name", "output_path" }
                }
            },
            new ToolInfo {
                Name = "dump_memory_to_file",
                Description = "Save a contiguous range of process memory directly to a file. Supports large ranges up to 256 MB. Useful for dumping unpacked payloads or large data buffers. Requires active debug session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["address"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Start address as hex (0x7FF000) or decimal" },
                        ["size"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Number of bytes to dump (1 to 268435456 / 256 MB)" },
                        ["output_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to write the memory dump" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID" }
                    },
                    ["required"] = new List<string> { "address", "size", "output_path" }
                }
            },
            new ToolInfo {
                Name = "get_local_variables",
                Description = "Read local variables and parameters from a paused debug session stack frame. Returns primitive values, strings, and addresses for complex objects. Requires the debugger to be paused at a breakpoint.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["frame_index"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Stack frame index (0 = innermost/current frame, default 0)" },
                        ["process_id"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID when multiple processes are being debugged" }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "eval_expression",
                Description = "Evaluate a C# expression in the context of the current paused stack frame, equivalent to the Watch window in dnSpy. Returns the value with type information. Supports field/property access, method calls (with func_eval), arithmetic, and casts. Requires the debugger to be paused.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["expression"]                = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "C# expression to evaluate (e.g. \"myObj.Field\", \"arr.Length\", \"(int)someValue\")" },
                        ["frame_index"]               = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Stack frame index (0 = innermost/current, default 0)" },
                        ["process_id"]                = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Optional: target process ID when multiple processes are being debugged" },
                        ["func_eval_timeout_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Timeout for function evaluation calls in the debuggee (default 5s). Increase if the evaluated expression involves slow methods." }
                    },
                    ["required"] = new List<string> { "expression" }
                }
            },
            new ToolInfo {
                Name = "unpack_from_memory",
                Description = "All-in-one unpacker for ConfuserEx and similar packers: launches the EXE under the debugger pausing at EntryPoint (after the module .cctor has decrypted method bodies), dumps the main module with PE-layout fix, and optionally stops the session. The output file contains readable IL and can be deobfuscated with deobfuscate_assembly.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["exe_path"]       = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to the packed/protected .NET Framework EXE" },
                        ["output_path"]    = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Optional path to write the unpacked EXE. Defaults to <original_name>_unpacked<ext> next to the input file." },
                        ["timeout_ms"]     = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max milliseconds to wait for the process to pause at entry point (default 30000)" },
                        ["stop_after_dump"]= new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Stop the debug session after dumping (default true)" },
                        ["module_name"]    = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Override module name to search for (default: EXE filename). Use list_runtime_modules to discover names if auto-detect fails." }
                    },
                    ["required"] = new List<string> { "exe_path" }
                }
            },
        };

        // ── Deobfuscation tools ───────────────────────────────────────────────────
        List<ToolInfo> GetDeobfuscationToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_deobfuscators",
                Description = "List all obfuscator types supported by the integrated de4dot engine (e.g. ConfuserEx, Dotfuscator, SmartAssembly, etc.).",
                InputSchema = new Dictionary<string, object> {
                    ["type"]       = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"]   = new List<string>()
                }
            },
            new ToolInfo {
                Name = "detect_obfuscator",
                Description = "Detect which obfuscator was applied to a .NET assembly file on disk. Uses de4dot's heuristic detection engine.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["file_path"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Absolute path to the target DLL or EXE" }
                    },
                    ["required"] = new List<string> { "file_path" }
                }
            },
            new ToolInfo {
                Name = "deobfuscate_assembly",
                Description = "Deobfuscate a .NET assembly using de4dot. Renames mangled symbols, deobfuscates control flow, and decrypts strings. Output is saved to disk.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["file_path"]             = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to the obfuscated DLL or EXE" },
                        ["output_path"]           = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Output path for the cleaned file (default: <name>-cleaned<ext> next to input)" },
                        ["method"]                = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Force a specific deobfuscator by Type, Name, or TypeLong (e.g. 'cr' for ConfuserEx). Auto-detected if omitted." },
                        ["obfuscator_type"]       = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Alias for 'method'. Accepts short de4dot codes such as 'cr'." },
                        ["rename_symbols"]        = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Rename obfuscated symbols (default true)" },
                        ["control_flow"]          = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Deobfuscate control flow (default true)" },
                        ["keep_obfuscator_types"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Keep obfuscator-internal types in the output (default false)" },
                        ["string_decrypter"]      = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "String decrypter mode: none | static | delegate | emulate (default static)" },
                        ["timeout_seconds"]       = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max seconds to wait for deobfuscation (default 120, minimum 10)." }
                    },
                    ["required"] = new List<string> { "file_path" }
                }
            },
            new ToolInfo {
                Name = "save_deobfuscated",
                Description = "Return a previously deobfuscated file as a Base64-encoded blob. Useful when the output file cannot be accessed directly.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["file_path"]   = new Dictionary<string, object> { ["type"] = "string",  ["description"] = "Absolute path to the already-deobfuscated file" },
                        ["max_size_mb"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Reject files larger than this many megabytes (default 50)" }
                    },
                    ["required"] = new List<string> { "file_path" }
                }
            },
        };

        // ── Skills knowledge base ─────────────────────────────────────────────────

        // ── Scripting tools ───────────────────────────────────────────────────────
        List<ToolInfo> GetScriptingToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "run_script",
                Description = "Execute arbitrary C# code via Roslyn inside dnSpy's process. " +
                    "Globals available in scripts: `module` (ModuleDef? — currently selected assembly, or null), " +
                    "`allModules` (IReadOnlyList<ModuleDef> — all loaded assemblies), " +
                    "`docService` (IDsDocumentService), `dbgManager` (DbgManager? — null when no debug session), " +
                    "`print(value)` / `print(fmt, args)` — capture output lines. " +
                    "Pre-imported namespaces: System, System.Linq, System.IO, System.Text, System.Collections.Generic, " +
                    "System.Reflection, dnlib.DotNet, dnlib.DotNet.Emit, dnlib.DotNet.Writer. " +
                    "Return value (if any) is appended to output as 'Return: <value>'. " +
                    "Requires 'enableRunScript': true in mcp-config.json. Disabled by default.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["code"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "C# code to execute inside dnSpy's process. Has full access to all dnSpy APIs and loaded assemblies."
                        },
                        ["timeout_seconds"] = new Dictionary<string, object> {
                            ["type"] = "integer",
                            ["description"] = "Maximum execution time in seconds. Default: 30."
                        }
                    },
                    ["required"] = new List<string> { "code" }
                }
            },
        };

        // ── Window / dialog management ────────────────────────────────────────────
        List<ToolInfo> GetWindowToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_dialogs",
                Description = "List active dialog/message-box windows in the dnSpy process. " +
                    "Returns title, HWND, message text and available button labels for each dialog.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "close_dialog",
                Description = "Close a dialog/message-box window by clicking a button. " +
                    "If no HWND given, closes the first active dialog found. " +
                    "Button matching is case-insensitive and supports common English button names: " +
                    "ok/accept, yes, no, cancel, retry, ignore.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["hwnd"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Hex HWND of specific dialog (from list_dialogs). Optional."
                        },
                        ["button"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Button to click: ok (default), accept, yes, no, cancel, retry, or ignore."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
        };

        // ── Utility tools ─────────────────────────────────────────────────────────
        List<ToolInfo> GetUtilityToolSchemas() => new List<ToolInfo> {
            new ToolInfo {
                Name = "list_tools",
                Description = "List the MCP tools currently visible to this session. In meta-only mode this returns only bootstrap tools plus any enabled tool groups.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "status",
                Description = "Return the current dnSpy MCP server status and tool-registration summary.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                },
                Annotations = new ToolAnnotations {
                    ReadOnlyHint = true,
                    DestructiveHint = false,
                    IdempotentHint = true,
                    OpenWorldHint = false
                },
                OutputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["server_name"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["version"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["is_running"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["status_message"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["registered_tool_count"] = new Dictionary<string, object> { ["type"] = "integer" },
                        ["visible_tool_count"] = new Dictionary<string, object> { ["type"] = "integer" },
                        ["discovery_mode"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["implicit_default_session_enabled"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["implicit_default_session_id"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["logging"] = new Dictionary<string, object> { ["type"] = "object" },
                        ["representative_tools"] = new Dictionary<string, object> {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object> { ["type"] = "string" }
                        }
                    },
                    ["required"] = new List<string> {
                        "server_name", "version", "is_running", "status_message", "registered_tool_count"
                    }
                }
            },
            new ToolInfo {
                Name = "get_server_stats",
                Description = "Return aggregate tool-catalog and service-availability statistics for the dnSpy MCP server.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                },
                Annotations = new ToolAnnotations {
                    ReadOnlyHint = true,
                    DestructiveHint = false,
                    IdempotentHint = true,
                    OpenWorldHint = false
                },
                OutputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["version"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["tool_stats"] = new Dictionary<string, object> { ["type"] = "object" },
                        ["service_availability"] = new Dictionary<string, object> { ["type"] = "object" }
                    },
                    ["required"] = new List<string> { "version", "tool_stats", "service_availability" }
                }
            },
            new ToolInfo {
                Name = "get_logging_status",
                Description = "Return the current logging and tool-execution telemetry settings without exposing unrelated server configuration.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                },
                Annotations = new ToolAnnotations {
                    ReadOnlyHint = true,
                    DestructiveHint = false,
                    IdempotentHint = true,
                    OpenWorldHint = false
                },
                OutputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["log_level"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["enable_file_logging"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["enable_output_pane_logging"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["enable_tool_call_logging"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["log_file_path"] = new Dictionary<string, object> { ["type"] = "string" }
                    },
                    ["required"] = new List<string> {
                        "log_level", "enable_file_logging", "enable_output_pane_logging", "enable_tool_call_logging"
                    }
                }
            },
            new ToolInfo {
                Name = "set_logging",
                Description = "Update runtime logging and tool-execution telemetry controls. This is intentionally narrower than exposing general server configuration.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["log_level"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Minimum level to emit: Debug, Info, Warning, or Error."
                        },
                        ["enable_file_logging"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "Whether to write dnspy_mcp.log."
                        },
                        ["enable_output_pane_logging"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "Whether to mirror logs to the dnSpy output pane."
                        },
                        ["enable_tool_call_logging"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "Whether to emit per-tool execution telemetry with redacted argument summaries."
                        },
                        ["persist"] = new Dictionary<string, object> {
                            ["type"] = "boolean",
                            ["description"] = "Persist the updated logging settings back to mcp-config.json. Default true."
                        }
                    },
                    ["required"] = new List<string>()
                },
                Annotations = new ToolAnnotations {
                    ReadOnlyHint = false,
                    DestructiveHint = false,
                    IdempotentHint = true,
                    OpenWorldHint = false
                },
                OutputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["updated"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["persisted"] = new Dictionary<string, object> { ["type"] = "boolean" },
                        ["logging"] = new Dictionary<string, object> { ["type"] = "object" }
                    },
                    ["required"] = new List<string> { "updated", "persisted", "logging" }
                }
            },
            new ToolInfo {
                Name = "get_code_mode_guide",
                Description = "Return structured guidance for dnspy_execute_code, the preferred first tool for multi-step dnSpy analysis. Includes helper functions, limits, common workflows, and likely failure patterns.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["topic"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional topic such as 'file parser', 'debugger attach', or 'rename obfuscated symbols' to tailor example selection."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_code_examples",
                Description = "Return curated dnspy_execute_code examples for common reverse-engineering workflows such as file-format tracing, render-pipeline reconstruction, symbol renaming, and runtime inspection.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["task"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional workflow description used to rank examples."
                        },
                        ["limit"] = new Dictionary<string, object> {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of examples to return. Default 4, maximum 10."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "validate_code_snippet",
                Description = "Compile-check a dnspy_execute_code snippet without running it. Returns diagnostics, referenced tool names, and likely fixes for common code-mode failures.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["code"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "C# snippet intended for dnspy_execute_code."
                        }
                    },
                    ["required"] = new List<string> { "code" }
                }
            },
            new ToolInfo {
                Name = "search_tools",
                Description = "Search the hidden full dnSpy capability catalog without enabling every tool up front. Use this first to discover the right tool group or exact tool for the workflow you need.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["query"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Search query describing the workflow or capability you need, for example 'custom file format parser', 'attach debugger', or 'anti tamper'."
                        },
                        ["detail"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Result detail level: brief, detailed, or full. Default brief."
                        },
                        ["group"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional tool-group filter."
                        },
                        ["limit"] = new Dictionary<string, object> {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of results to return. Default 12, maximum 100."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "get_tool_schemas",
                Description = "Fetch schemas and detailed metadata for specific tools, even when they are not currently enabled for direct invocation.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["tool_names"] = new Dictionary<string, object> {
                            ["type"] = "array",
                            ["description"] = "Public dnspy_* tool names to inspect.",
                            ["items"] = new Dictionary<string, object> {
                                ["type"] = "string"
                            }
                        },
                        ["detail"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Schema detail level: detailed or full. Default full."
                        }
                    },
                    ["required"] = new List<string> { "tool_names" }
                }
            },
            new ToolInfo {
                Name = "list_tool_groups",
                Description = "List stable dnSpy tool groups, their intended workflows, and representative tools. Use this before enabling a whole workflow area for the session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>(),
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "enable_tool_groups",
                Description = "Enable one or more workflow tool groups for the current session so they become visible through tools/list and directly callable through tools/call. Prefer dnspy_execute_code first; use this when you explicitly want direct tool exposure outside code mode.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["groups"] = new Dictionary<string, object> {
                            ["type"] = "array",
                            ["description"] = "Tool groups to enable for this session, for example source_and_decompile, reconstruction_core, architecture_and_xrefs, or debug_runtime.",
                            ["items"] = new Dictionary<string, object> {
                                ["type"] = "string"
                            }
                        },
                        ["session_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional explicit session id. Usually omitted when using the SSE /message endpoint. In stateless HTTP mode the server can fall back to an implicit default session."
                        }
                    },
                    ["required"] = new List<string> { "groups" }
                }
            },
            new ToolInfo {
                Name = "disable_tool_groups",
                Description = "Disable one or more previously enabled tool groups for the current session.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["groups"] = new Dictionary<string, object> {
                            ["type"] = "array",
                            ["description"] = "Tool groups to disable for this session.",
                            ["items"] = new Dictionary<string, object> {
                                ["type"] = "string"
                            }
                        },
                        ["session_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional explicit session id. Usually omitted when using the SSE /message endpoint. In stateless HTTP mode the server can fall back to an implicit default session."
                        }
                    },
                    ["required"] = new List<string> { "groups" }
                }
            },
            new ToolInfo {
                Name = "get_enabled_tool_groups",
                Description = "Return the tool groups currently enabled for this session and the effective visible tool list.",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["session_id"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "Optional explicit session id. Usually omitted when using the SSE /message endpoint. In stateless HTTP mode the server can fall back to an implicit default session."
                        }
                    },
                    ["required"] = new List<string>()
                }
            },
            new ToolInfo {
                Name = "execute_code",
                Description = "Run constrained C# code mode for multi-step dnSpy workflows. This is the preferred first tool after initialize when the task needs discovery plus several dnSpy operations. Scripts can print output and call other dnSpy tools with await call_tool(\"dnspy_get_cfg\", new { ... }).",
                InputSchema = new Dictionary<string, object> {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object> {
                        ["code"] = new Dictionary<string, object> {
                            ["type"] = "string",
                            ["description"] = "C# code to execute. Use print(...) for textual output and await call_tool(name, args) to compose dnSpy tools."
                        },
                        ["timeout_seconds"] = new Dictionary<string, object> {
                            ["type"] = "integer",
                            ["description"] = "Maximum execution time in seconds. Default 20, maximum 120."
                        }
                    },
                    ["required"] = new List<string> { "code" }
                }
            },
        };
    }
}
