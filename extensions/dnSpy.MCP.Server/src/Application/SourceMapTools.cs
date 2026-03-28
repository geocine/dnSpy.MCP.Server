using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.Json;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Scripting;
using dnSpy.MCP.Server.Contracts;
using dnSpy.MCP.Server.Helper;
using HoLLy.dnSpyExtension.Contracts;

namespace dnSpy.MCP.Server.Application
{
    [Export(typeof(SourceMapTools))]
    public sealed class SourceMapTools
    {
        const string SourceMapDecompilerSuffix = "(w/ SourceMap)";
        readonly IServiceLocator serviceLocator;
        readonly IDocumentTreeView documentTreeView;
        readonly IDecompilerService decompilerService;

        [ImportingConstructor]
        public SourceMapTools(IServiceLocator serviceLocator, IDocumentTreeView documentTreeView, IDecompilerService decompilerService)
        {
            this.serviceLocator = serviceLocator;
            this.documentTreeView = documentTreeView;
            this.decompilerService = decompilerService;
        }

        public CallToolResult SourceMapStatus(Dictionary<string, object>? arguments)
        {
            _ = arguments;
            var service = TryGetSourceMapService();
            var selectedDecompiler = FindSourceMapDecompiler();
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["holly_service_available"] = service != null,
                ["source_map_decompiler_available"] = selectedDecompiler != null,
                ["active_decompiler"] = decompilerService.Decompiler.UniqueNameUI,
                ["selected_source_map_decompiler"] = selectedDecompiler?.UniqueNameUI,
                ["available_source_map_decompilers"] = decompilerService.AllDecompilers
                    .Where(IsSourceMapDecompiler)
                    .Select(a => a.UniqueNameUI)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ["cache_folder"] = service?.CacheFolder,
                ["supports_parameter_display_names"] = service?.SupportsParameterDisplayNames ?? false
            });
        }

        public CallToolResult SourceMapDecompileType(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var decompiler = RequireSourceMapDecompiler();
            var assembly = FindAssemblyByName(RequireString(arguments, "assembly_name"), OptionalString(arguments, "file_path"))
                ?? throw new ArgumentException("Assembly not found.");
            var type = FindTypeInAssemblyAll(assembly, RequireString(arguments, "type_full_name"))
                ?? throw new ArgumentException("Type not found.");
            return ToolResponseFactory.Text(DecompileType(type, decompiler));
        }

        public CallToolResult SourceMapDecompileMethod(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var decompiler = RequireSourceMapDecompiler();
            var assembly = FindAssemblyByName(RequireString(arguments, "assembly_name"), OptionalString(arguments, "file_path"))
                ?? throw new ArgumentException("Assembly not found.");
            var type = FindTypeInAssemblyAll(assembly, RequireString(arguments, "type_full_name"))
                ?? throw new ArgumentException("Type not found.");
            var methodName = RequireString(arguments, "method_name");
            var signature = OptionalString(arguments, "signature");
            var methods = type.Methods.Where(a => a.Name.String.Equals(methodName, StringComparison.Ordinal)).ToList();
            if (!string.IsNullOrWhiteSpace(signature))
                methods = methods.Where(a => a.FullName.Equals(signature, StringComparison.Ordinal)).ToList();
            if (methods.Count == 0)
                throw new ArgumentException($"Method '{methodName}' not found in '{type.FullName}'.");

            var chunks = methods.Select(a =>
            {
                var text = DecompileMethod(a, decompiler);
                return methods.Count > 1 ? $"// Overload: {a.FullName}{Environment.NewLine}{text}" : text;
            });
            return ToolResponseFactory.Text(string.Join(Environment.NewLine, chunks));
        }

        public CallToolResult SourceMapGetDecompiledSource(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("member_id or symbol reference arguments are required");

            var service = RequireSourceMapService();
            var decompiler = RequireSourceMapDecompiler();
            var resolved = ResolveReference(arguments);
            var payload = DescribeReferenceWithMemberId(resolved, service);
            payload["source"] = DecompileResolvedReference(resolved, decompiler);
            payload["decompile_scope"] = GetDecompileScope(resolved);
            payload["decompiler"] = decompiler.UniqueNameUI;
            payload["patch_mode"] = "sourcemap";
            return ToolResponseFactory.Json(payload);
        }

        public CallToolResult SourceMapBatchGetDecompiledSource(Dictionary<string, object>? arguments)
        {
            if (arguments == null || !arguments.TryGetValue("member_ids", out var memberIdsObj))
                throw new ArgumentException("member_ids is required");

            var service = RequireSourceMapService();
            var decompiler = RequireSourceMapDecompiler();
            var memberIds = ReadStringArray(memberIdsObj);
            if (memberIds.Count == 0)
                throw new ArgumentException("member_ids must contain at least one member_id");

            var items = new List<Dictionary<string, object?>>();
            foreach (var memberId in memberIds)
            {
                try
                {
                    var resolved = ResolveMemberId(memberId);
                    var payload = DescribeReferenceWithMemberId(resolved, service);
                    payload["source"] = DecompileResolvedReference(resolved, decompiler);
                    payload["decompile_scope"] = GetDecompileScope(resolved);
                    payload["decompiler"] = decompiler.UniqueNameUI;
                    payload["patch_mode"] = "sourcemap";
                    items.Add(payload);
                }
                catch (Exception ex)
                {
                    items.Add(new Dictionary<string, object?> {
                        ["member_id"] = memberId,
                        ["error"] = ex.Message
                    });
                }
            }

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["items"] = items,
                ["requested_count"] = memberIds.Count,
                ["returned_count"] = items.Count,
                ["decompiler"] = decompiler.UniqueNameUI
            });
        }

        public CallToolResult SourceMapRenameMember(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            _ = RequireSourceMapDecompiler();
            var assembly = FindAssemblyByName(RequireString(arguments, "assembly_name"), OptionalString(arguments, "file_path"))
                ?? throw new ArgumentException("Assembly not found.");
            var type = FindTypeInAssemblyAll(assembly, RequireString(arguments, "type_full_name"))
                ?? throw new ArgumentException("Type not found.");
            var oldName = RequireString(arguments, "old_name");
            var memberKind = RequireString(arguments, "member_kind").ToLowerInvariant();
            var member = memberKind switch
            {
                "type" => (IMemberDef)type,
                "method" => type.Methods.FirstOrDefault(a => a.Name.String.Equals(oldName, StringComparison.Ordinal)),
                "field" => type.Fields.FirstOrDefault(a => a.Name.String.Equals(oldName, StringComparison.Ordinal)),
                "property" => type.Properties.FirstOrDefault(a => a.Name.String.Equals(oldName, StringComparison.Ordinal)),
                "event" => type.Events.FirstOrDefault(a => a.Name.String.Equals(oldName, StringComparison.Ordinal)),
                _ => throw new ArgumentException($"Invalid member_kind: '{memberKind}'.")
            } ?? throw new ArgumentException($"Unable to resolve '{oldName}' in '{type.FullName}'.");
            return RenameMemberDisplayName(service, member, RequireString(arguments, "new_name"), "sourcemap_rename_member");
        }

        public CallToolResult SourceMapRenameMethod(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            _ = RequireSourceMapDecompiler();
            var assembly = FindAssemblyByName(RequireString(arguments, "assembly_name"), OptionalString(arguments, "file_path"))
                ?? throw new ArgumentException("Assembly not found.");
            var type = FindTypeInAssemblyAll(assembly, RequireString(arguments, "type_full_name"))
                ?? throw new ArgumentException("Type not found.");
            var methodName = RequireString(arguments, "method_name");
            var tokenText = OptionalString(arguments, "method_token");
            MethodDef method;
            if (!string.IsNullOrWhiteSpace(tokenText))
            {
                var token = ParseMetadataToken(tokenText!);
                method = type.Methods.FirstOrDefault(a => a.MDToken.Raw == token)
                    ?? throw new ArgumentException($"No method with token '{tokenText}' found in '{type.FullName}'.");
            }
            else
            {
                var matches = type.Methods.Where(a => a.Name.String.Equals(methodName, StringComparison.Ordinal)).ToList();
                if (matches.Count != 1)
                    throw new ArgumentException(matches.Count == 0
                        ? $"Method '{methodName}' not found in '{type.FullName}'."
                        : $"Method '{methodName}' is ambiguous. Provide method_token.");
                method = matches[0];
            }
            return RenameMemberDisplayName(service, method, RequireString(arguments, "new_name"), "sourcemap_rename_method");
        }

        public CallToolResult SourceMapRenameSymbol(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            _ = RequireSourceMapDecompiler();
            var resolved = ResolveReference(arguments);
            if (resolved is not IMemberDef member)
                throw new ArgumentException("sourcemap_rename_symbol requires a type, method, field, property, or event target.");
            return RenameMemberDisplayName(service, member, RequireString(arguments, "new_name"), "sourcemap_rename_symbol");
        }

        public CallToolResult SourceMapRenameParameter(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            _ = RequireSourceMapDecompiler();
            var method = ResolveReference(arguments) as MethodDef
                ?? throw new ArgumentException("sourcemap_rename_parameter requires a method member_id or legacy method reference.");
            var paramDefs = method.ParamDefs.Where(a => a.Sequence > 0).OrderBy(a => a.Sequence).ToList();
            if (paramDefs.Count == 0)
                throw new ArgumentException("This method has no metadata-backed parameter definitions to rename.");

            ParamDef parameter;
            if (arguments.TryGetValue("parameter_index", out var indexObj))
            {
                var index = ParseIntArg(indexObj, "parameter_index");
                if (index < 0 || index >= paramDefs.Count)
                    throw new ArgumentException($"parameter_index out of range. Method has {paramDefs.Count} parameter definitions.");
                parameter = paramDefs[index];
            }
            else if (arguments.TryGetValue("old_name", out var oldNameObj))
            {
                var oldName = oldNameObj?.ToString() ?? string.Empty;
                parameter = paramDefs.FirstOrDefault(a =>
                        string.Equals(a.Name.String, oldName, StringComparison.Ordinal) ||
                        string.Equals(service.GetParameterDisplayName(method, a.Sequence), oldName, StringComparison.Ordinal))
                    ?? throw new ArgumentException($"Parameter '{oldName}' not found. Available: {string.Join(", ", paramDefs.Select(a => service.GetParameterDisplayName(method, a.Sequence) ?? a.Name.String))}");
            }
            else
            {
                throw new ArgumentException("parameter_index or old_name is required");
            }

            if (service.SupportsParameterDisplayNames)
            {
                var newName = RequireString(arguments, "new_name");
                var displayNameBefore = service.GetParameterDisplayName(method, parameter.Sequence) ?? parameter.Name.String;
                service.SetParameterDisplayName(method, parameter.Sequence, newName);
                return ToolResponseFactory.Json(new Dictionary<string, object?> {
                    ["status"] = "updated",
                    ["patch_mode"] = "sourcemap",
                    ["method_member_id"] = BuildMemberId(method.Module, method.MDToken.Raw, 'M'),
                    ["method"] = method.FullName,
                    ["parameter_sequence"] = parameter.Sequence,
                    ["old_name"] = parameter.Name.String,
                    ["display_name_before"] = displayNameBefore,
                    ["display_name_after"] = service.GetParameterDisplayName(method, parameter.Sequence) ?? newName,
                    ["new_name"] = newName,
                    ["persisted"] = true,
                    ["persisted_to"] = "holly_cache",
                    ["cache_folder"] = service.CacheFolder
                });
            }

            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["status"] = "not_supported_yet",
                ["patch_mode"] = "sourcemap",
                ["reason"] = "holly_parameter_sourcemap_not_implemented",
                ["method_member_id"] = BuildMemberId(method.Module, method.MDToken.Raw, 'M'),
                ["method"] = method.FullName,
                ["parameter_sequence"] = parameter.Sequence,
                ["old_name"] = parameter.Name.String,
                ["new_name"] = RequireString(arguments, "new_name"),
                ["supports_parameter_display_names"] = false,
                ["message"] = "HoLLy does not yet support SourceMap-backed parameter renaming. MCP intentionally did not fall back to binary metadata renaming."
            }, isError: true);
        }

        public CallToolResult SourceMapExport(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            var assembly = ResolveAssembly(arguments);
            var outputPath = RequireString(arguments, "output_path");
            service.ExportSourceMap(assembly, outputPath);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["status"] = "exported",
                ["assembly_name"] = assembly.Name.String,
                ["assembly_full_name"] = assembly.FullName,
                ["output_path"] = Path.GetFullPath(outputPath),
                ["cache_folder"] = service.CacheFolder,
                ["patch_mode"] = "sourcemap"
            });
        }

        public CallToolResult SourceMapImport(Dictionary<string, object>? arguments)
        {
            if (arguments == null)
                throw new ArgumentException("Arguments required");

            var service = RequireSourceMapService();
            var assembly = ResolveAssembly(arguments);
            var inputPath = RequireString(arguments, "input_path");
            if (!File.Exists(inputPath))
                throw new ArgumentException($"File not found: {inputPath}");
            service.ImportSourceMap(assembly, inputPath);
            return ToolResponseFactory.Json(new Dictionary<string, object?> {
                ["status"] = "imported",
                ["assembly_name"] = assembly.Name.String,
                ["assembly_full_name"] = assembly.FullName,
                ["input_path"] = Path.GetFullPath(inputPath),
                ["cache_folder"] = service.CacheFolder,
                ["patch_mode"] = "sourcemap",
                ["persisted_to"] = "holly_cache"
            });
        }

        CallToolResult RenameMemberDisplayName(IHoLLySourceMapService service, IMemberDef member, string newName, string toolName)
        {
            var displayNameBefore = service.GetMemberDisplayName(member) ?? GetMetadataName(member);
            service.SetMemberDisplayName(member, newName);
            var payload = DescribeReferenceWithMemberId(member, service);
            payload["tool_name"] = toolName;
            payload["patch_mode"] = "sourcemap";
            payload["display_name_before"] = displayNameBefore;
            payload["display_name_after"] = service.GetMemberDisplayName(member) ?? GetMetadataName(member);
            payload["metadata_name"] = GetMetadataName(member);
            payload["persisted"] = true;
            payload["persisted_to"] = "holly_cache";
            payload["cache_folder"] = service.CacheFolder;
            payload["save_assembly_required"] = false;
            return ToolResponseFactory.Json(payload);
        }

        IHoLLySourceMapService RequireSourceMapService() =>
            TryGetSourceMapService() ?? throw new InvalidOperationException(
                "HoLLy SourceMap service is not available. Make sure dnSpy.Extension.HoLLy is installed and loaded.");

        IHoLLySourceMapService? TryGetSourceMapService() => serviceLocator.TryResolve<IHoLLySourceMapService>();

        IDecompiler RequireSourceMapDecompiler() =>
            FindSourceMapDecompiler() ?? throw new InvalidOperationException(
                "No HoLLy SourceMap decompiler is available. Install dnSpy.Extension.HoLLy and select a compatible decompiler variant.");

        IDecompiler? FindSourceMapDecompiler()
        {
            var decompilers = decompilerService.AllDecompilers.ToList();
            var active = decompilerService.Decompiler;
            var match = decompilers.FirstOrDefault(a => IsSourceMapDecompiler(a) && (a.UniqueGuid == active.UniqueGuid || a.GenericGuid == active.GenericGuid));
            if (match != null)
                return match;
            match = decompilers.FirstOrDefault(a => IsSourceMapDecompiler(a) && string.Equals(a.GenericNameUI, active.GenericNameUI, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
            match = decompilers.FirstOrDefault(a => IsSourceMapDecompiler(a) && string.Equals(a.GenericNameUI, "C#", StringComparison.OrdinalIgnoreCase));
            return match ?? decompilers.FirstOrDefault(IsSourceMapDecompiler);
        }

        static bool IsSourceMapDecompiler(IDecompiler decompiler) =>
            decompiler.UniqueNameUI.EndsWith(SourceMapDecompilerSuffix, StringComparison.OrdinalIgnoreCase);

        AssemblyDef ResolveAssembly(Dictionary<string, object> arguments)
        {
            if (arguments.TryGetValue("file_path", out var filePathObj) && !string.IsNullOrWhiteSpace(filePathObj?.ToString()))
            {
                var byPath = FindAssemblyByPath(filePathObj!.ToString()!);
                if (byPath != null)
                    return byPath;
            }

            return FindAssemblyByName(RequireString(arguments, "assembly_name"))
                ?? throw new ArgumentException("Assembly not found.");
        }

        object ResolveReference(Dictionary<string, object> arguments)
        {
            if (arguments.TryGetValue("member_id", out var memberIdObj) && !string.IsNullOrWhiteSpace(memberIdObj?.ToString()))
                return ResolveMemberId(memberIdObj!.ToString()!);

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

            var type = FindTypeInAssemblyAll(assembly, RequireString(arguments, "type_full_name"))
                ?? throw new ArgumentException("Type not found.");

            if (arguments.TryGetValue("method_name", out var methodObj) && !string.IsNullOrWhiteSpace(methodObj?.ToString()))
            {
                var methodName = methodObj!.ToString()!;
                var signature = OptionalString(arguments, "method_signature") ?? OptionalString(arguments, "signature");
                var methods = type.Methods.Where(a => a.Name.String.Equals(methodName, StringComparison.Ordinal)).ToList();
                if (!string.IsNullOrWhiteSpace(signature))
                    methods = methods.Where(a => string.Equals(a.MethodSig?.ToString(), signature, StringComparison.Ordinal) || string.Equals(a.FullName, signature, StringComparison.Ordinal)).ToList();
                if (methods.Count == 0)
                    throw new ArgumentException($"Method '{methodName}' not found in '{type.FullName}'.");
                if (methods.Count > 1)
                    throw new ArgumentException($"Method '{methodName}' is ambiguous. Provide method_signature or member_id.");
                return methods[0];
            }

            if (arguments.TryGetValue("field_name", out var fieldObj) && !string.IsNullOrWhiteSpace(fieldObj?.ToString()))
                return type.Fields.FirstOrDefault(a => a.Name.String.Equals(fieldObj!.ToString(), StringComparison.Ordinal))
                    ?? throw new ArgumentException($"Field '{fieldObj}' not found in '{type.FullName}'.");

            if (arguments.TryGetValue("property_name", out var propertyObj) && !string.IsNullOrWhiteSpace(propertyObj?.ToString()))
                return type.Properties.FirstOrDefault(a => a.Name.String.Equals(propertyObj!.ToString(), StringComparison.Ordinal))
                    ?? throw new ArgumentException($"Property '{propertyObj}' not found in '{type.FullName}'.");

            if (arguments.TryGetValue("event_name", out var eventObj) && !string.IsNullOrWhiteSpace(eventObj?.ToString()))
                return type.Events.FirstOrDefault(a => a.Name.String.Equals(eventObj!.ToString(), StringComparison.Ordinal))
                    ?? throw new ArgumentException($"Event '{eventObj}' not found in '{type.FullName}'.");

            if (arguments.TryGetValue("member_kind", out var memberKindObj) && arguments.TryGetValue("member_name", out var memberNameObj))
            {
                var memberKind = memberKindObj?.ToString()?.ToLowerInvariant() ?? string.Empty;
                var memberName = memberNameObj?.ToString() ?? string.Empty;
                return memberKind switch
                {
                    "type" => type,
                    "method" => type.Methods.FirstOrDefault(a => a.Name.String.Equals(memberName, StringComparison.Ordinal))
                        ?? throw new ArgumentException($"Method '{memberName}' not found in '{type.FullName}'."),
                    "field" => type.Fields.FirstOrDefault(a => a.Name.String.Equals(memberName, StringComparison.Ordinal))
                        ?? throw new ArgumentException($"Field '{memberName}' not found in '{type.FullName}'."),
                    "property" => type.Properties.FirstOrDefault(a => a.Name.String.Equals(memberName, StringComparison.Ordinal))
                        ?? throw new ArgumentException($"Property '{memberName}' not found in '{type.FullName}'."),
                    "event" => type.Events.FirstOrDefault(a => a.Name.String.Equals(memberName, StringComparison.Ordinal))
                        ?? throw new ArgumentException($"Event '{memberName}' not found in '{type.FullName}'."),
                    _ => throw new ArgumentException($"Invalid member_kind: '{memberKind}'.")
                };
            }

            return type;
        }

        object ResolveMemberId(string memberId)
        {
            var parts = memberId.Split(':');
            if (parts.Length != 3)
                throw new ArgumentException($"Invalid member_id format: {memberId}");
            if (!Guid.TryParseExact(parts[0], "N", out var mvid))
                throw new ArgumentException($"Invalid module MVID in member_id: {memberId}");
            if (!uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var rawToken))
                throw new ArgumentException($"Invalid metadata token in member_id: {memberId}");
            var module = FindLoadedModuleByMvid(mvid)
                ?? throw new ArgumentException($"No loaded module found for MVID {mvid:N}. Load the assembly first.");
            return module.ResolveToken(rawToken)
                ?? throw new ArgumentException($"Metadata token 0x{rawToken:X8} could not be resolved in module {module.Name}.");
        }

        Dictionary<string, object?> DescribeReferenceWithMemberId(object resolved, IHoLLySourceMapService? service)
        {
            return resolved switch
            {
                TypeDef typeDef => BuildMemberPayload(typeDef, typeDef.Module, typeDef.MDToken.Raw, 'T', service, new Dictionary<string, object?> {
                    ["assembly_name"] = typeDef.Module.Assembly?.Name.String,
                    ["module_name"] = typeDef.Module.Name,
                    ["name"] = typeDef.Name.String,
                    ["full_name"] = typeDef.FullName,
                    ["namespace"] = typeDef.Namespace.String,
                    ["declaring_type"] = typeDef.DeclaringType?.FullName
                }),
                MethodDef methodDef => BuildMemberPayload(methodDef, methodDef.Module, methodDef.MDToken.Raw, 'M', service, new Dictionary<string, object?> {
                    ["assembly_name"] = methodDef.Module.Assembly?.Name.String,
                    ["module_name"] = methodDef.Module.Name,
                    ["name"] = methodDef.Name.String,
                    ["full_name"] = methodDef.FullName,
                    ["declaring_type"] = methodDef.DeclaringType?.FullName,
                    ["signature"] = methodDef.MethodSig?.ToString()
                }),
                FieldDef fieldDef => BuildMemberPayload(fieldDef, fieldDef.Module, fieldDef.MDToken.Raw, 'F', service, new Dictionary<string, object?> {
                    ["assembly_name"] = fieldDef.Module.Assembly?.Name.String,
                    ["module_name"] = fieldDef.Module.Name,
                    ["name"] = fieldDef.Name.String,
                    ["full_name"] = fieldDef.FullName,
                    ["declaring_type"] = fieldDef.DeclaringType?.FullName,
                    ["field_type"] = fieldDef.FieldType?.FullName
                }),
                PropertyDef propertyDef => BuildMemberPayload(propertyDef, propertyDef.Module, propertyDef.MDToken.Raw, 'P', service, new Dictionary<string, object?> {
                    ["assembly_name"] = propertyDef.Module.Assembly?.Name.String,
                    ["module_name"] = propertyDef.Module.Name,
                    ["name"] = propertyDef.Name.String,
                    ["full_name"] = propertyDef.FullName,
                    ["declaring_type"] = propertyDef.DeclaringType?.FullName,
                    ["property_type"] = propertyDef.PropertySig?.RetType?.FullName
                }),
                EventDef eventDef => BuildMemberPayload(eventDef, eventDef.Module, eventDef.MDToken.Raw, 'E', service, new Dictionary<string, object?> {
                    ["assembly_name"] = eventDef.Module.Assembly?.Name.String,
                    ["module_name"] = eventDef.Module.Name,
                    ["name"] = eventDef.Name.String,
                    ["full_name"] = eventDef.FullName,
                    ["declaring_type"] = eventDef.DeclaringType?.FullName,
                    ["event_type"] = eventDef.EventType?.FullName
                }),
                _ => new Dictionary<string, object?> {
                    ["reference_kind"] = resolved.GetType().Name,
                    ["display_text"] = resolved.ToString()
                }
            };
        }

        static Dictionary<string, object?> BuildMemberPayload(IMemberDef member, ModuleDef module, uint rawToken, char kind, IHoLLySourceMapService? service, Dictionary<string, object?> payload)
        {
            payload["reference_kind"] = member.GetType().Name;
            payload["member_id"] = BuildMemberId(module, rawToken, kind);
            payload["metadata_name"] = GetMetadataName(member);
            payload["display_name"] = service?.GetMemberDisplayName(member) ?? GetMetadataName(member);
            return payload;
        }

        string DecompileResolvedReference(object resolved, IDecompiler decompiler)
        {
            return resolved switch
            {
                TypeDef typeDef => DecompileType(typeDef, decompiler),
                MethodDef methodDef => DecompileMethod(methodDef, decompiler),
                FieldDef fieldDef when fieldDef.DeclaringType != null => DecompileType(fieldDef.DeclaringType, decompiler),
                PropertyDef propertyDef when propertyDef.DeclaringType != null => DecompileType(propertyDef.DeclaringType, decompiler),
                EventDef eventDef when eventDef.DeclaringType != null => DecompileType(eventDef.DeclaringType, decompiler),
                _ => throw new ArgumentException($"Decompilation is not supported for resolved reference kind '{resolved.GetType().Name}'.")
            };
        }

        static string DecompileType(TypeDef type, IDecompiler decompiler)
        {
            var output = new StringBuilderDecompilerOutput();
            var context = new DecompilationContext { CancellationToken = System.Threading.CancellationToken.None };
            decompiler.Decompile(type, output, context);
            return output.ToString();
        }

        static string DecompileMethod(MethodDef method, IDecompiler decompiler)
        {
            var output = new StringBuilderDecompilerOutput();
            var context = new DecompilationContext { CancellationToken = System.Threading.CancellationToken.None };
            decompiler.Decompile(method, output, context);
            return output.ToString();
        }

        static string GetDecompileScope(object resolved) => resolved switch
        {
            TypeDef => "type",
            MethodDef => "method",
            FieldDef => "declaring_type",
            PropertyDef => "declaring_type",
            EventDef => "declaring_type",
            _ => "unsupported"
        };

        AssemblyDef? FindAssemblyByName(string name, string? filePath = null)
        {
            return UiThreadHelper.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    var normalizedPath = filePath.Replace('/', '\\');
                    var byPath = documentTreeView.GetAllModuleNodes()
                        .FirstOrDefault(a => (a.Document?.Filename ?? string.Empty).Replace('/', '\\')
                            .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
                    if (byPath?.Document?.AssemblyDef != null)
                        return byPath.Document.AssemblyDef;
                }

                return documentTreeView.GetAllModuleNodes()
                    .Select(a => a.Document?.AssemblyDef)
                    .FirstOrDefault(a => a != null && a.Name.String.Equals(name, StringComparison.OrdinalIgnoreCase));
            });
        }

        AssemblyDef? FindAssemblyByPath(string filePath)
        {
            return UiThreadHelper.Invoke(() =>
            {
                var normalizedPath = filePath.Replace('/', '\\');
                var node = documentTreeView.GetAllModuleNodes()
                    .FirstOrDefault(a => (a.Document?.Filename ?? string.Empty).Replace('/', '\\')
                        .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
                return node?.Document?.AssemblyDef;
            });
        }

        TypeDef? FindTypeInAssemblyAll(AssemblyDef assembly, string fullName) =>
            assembly.Modules.SelectMany(a => GetAllTypesRecursive(a.Types))
                .FirstOrDefault(a => a.FullName.Equals(fullName, StringComparison.Ordinal));

        static IEnumerable<TypeDef> GetAllTypesRecursive(IEnumerable<TypeDef> types)
        {
            foreach (var type in types)
            {
                yield return type;
                foreach (var nested in GetAllTypesRecursive(type.NestedTypes))
                    yield return nested;
            }
        }

        ModuleDef? FindLoadedModuleByMvid(Guid mvid) =>
            UiThreadHelper.Invoke(() =>
                documentTreeView.GetAllModuleNodes()
                    .Select(a => a.Document?.ModuleDef)
                    .FirstOrDefault(a => a != null && (a.Mvid ?? Guid.Empty) == mvid));

        static string GetMetadataName(IMemberDef member) => member switch
        {
            TypeDef type => type.Name.String,
            MethodDef method => method.Name.String,
            FieldDef field => field.Name.String,
            PropertyDef property => property.Name.String,
            EventDef @event => @event.Name.String,
            _ => member.Name.String
        };

        static string BuildMemberId(ModuleDef module, uint rawToken, char kind) =>
            $"{(module.Mvid ?? Guid.Empty):N}:{rawToken:X8}:{kind}";

        static uint ParseMetadataToken(object token)
        {
            if (token is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out var numeric))
                    return numeric;
                if (element.ValueKind == JsonValueKind.String)
                    return ParseMetadataToken(element.GetString() ?? string.Empty);
            }

            if (token is string text)
            {
                var trimmed = text.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
                    return hex;
                if (uint.TryParse(trimmed, out var numeric))
                    return numeric;
            }

            throw new ArgumentException($"Invalid metadata token: {token}");
        }

        static List<string> ReadStringArray(object value)
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.String)
                    .Select(a => a.GetString() ?? string.Empty)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
            }

            if (value is IEnumerable<string> stringValues)
                return stringValues.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (value is IEnumerable<object> objectValues)
                return objectValues.Select(a => a?.ToString() ?? string.Empty).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            return new List<string>();
        }

        static string RequireString(Dictionary<string, object> arguments, string key)
        {
            if (!arguments.TryGetValue(key, out var value) || value == null)
                throw new ArgumentException($"{key} is required");
            var text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException($"{key} cannot be empty");
            return text!;
        }

        static string? OptionalString(Dictionary<string, object> arguments, string key) =>
            arguments.TryGetValue(key, out var value) ? value?.ToString() : null;

        static int ParseIntArg(object value, string argumentName)
        {
            if (value is int i)
                return i;
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt))
                    return jsonInt;
                if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsedJson))
                    return parsedJson;
            }

            if (int.TryParse(value.ToString(), out var parsed))
                return parsed;
            throw new ArgumentException($"{argumentName} must be an integer");
        }
    }
}
