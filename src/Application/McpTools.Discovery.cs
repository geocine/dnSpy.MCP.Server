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
using System.Linq;
using System.Text.Json;
using dnSpy.MCP.Server.Contracts;

namespace dnSpy.MCP.Server.Application {
	public sealed partial class McpTools {
		readonly Dictionary<string, HashSet<string>> enabledToolGroupsBySession = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		readonly object enabledToolGroupsLock = new object();
		static readonly string[] initialCodeModeBootstrapToolOrder = new[] {
			"dnspy_execute_code",
			"dnspy_get_code_mode_guide",
			"dnspy_get_code_examples",
			"dnspy_validate_code_snippet",
			"dnspy_enable_tool_groups",
			"dnspy_get_enabled_tool_groups",
			"dnspy_status",
			"dnspy_get_server_stats",
			"dnspy_get_logging_status",
		};
		static readonly HashSet<string> initialCodeModeBootstrapTools = new HashSet<string>(initialCodeModeBootstrapToolOrder, StringComparer.Ordinal);

		static readonly Dictionary<string, string> toolGroupDescriptions = new Dictionary<string, string>(StringComparer.Ordinal) {
			["bootstrap"] = "Discovery and control-plane tools used to search capabilities, fetch schemas, validate code snippets, retrieve code-mode guidance/examples, and enable tool groups for the current session.",
			["reconstruction_core"] = "Assembly loading, identity normalization, startup analysis, and core reconstruction helpers for turning binaries into navigable software artifacts.",
			["source_and_decompile"] = "Source recovery, type/member decompilation, and structure inspection tools.",
			["architecture_and_xrefs"] = "Callers, callees, usages, inheritance, and dependency analysis for recovering program architecture and logic flow.",
			["metadata_and_native"] = "Low-level PE, metadata, heap, CFG, and native import/export inspection.",
			["deobfuscation_and_recovery"] = "Obfuscation detection, anti-debug and anti-tamper heuristics, semantic relabeling, and protection analysis.",
			["provenance_and_correlation"] = "Known-binary matching, third-party component labeling, and framework/package/source provenance recovery.",
			["editing_and_patchback"] = "Symbol renaming, metadata editing, resource work, and patchback into the binary for iterative reconstruction.",
			["debug_runtime"] = "Attach, break, step, inspect frames, locals, and runtime state when static analysis is insufficient.",
			["memory_and_dumping"] = "Read, write, dump, and unpack process memory and runtime-loaded modules.",
			["ui_navigation"] = "Drive dnSpy document selection and follow references through host services instead of brittle UI automation.",
			["scripting_advanced"] = "Advanced internal scripting surfaces. Not discoverable by default.",
			["legacy_compat"] = "Compatibility or catch-all tools that do not fit the primary reconstruction workflows."
		};

		public List<ToolInfo> GetAvailableTools(string? sessionId) {
			var catalog = GetToolCatalog();
			if (Configuration.McpConfig.Instance.ExposeFullToolCatalog)
				return catalog.Tools.ToList();

			var visibleTools = catalog.Tools
				.Where(a => IsToolVisibleByPublicName(a.Name, sessionId))
				.ToList();

			if (!HasAnyWorkflowGroupsEnabled(sessionId)) {
				return visibleTools
					.Where(a => initialCodeModeBootstrapTools.Contains(a.Name))
					.OrderBy(a => GetInitialCodeModeBootstrapOrder(a.Name))
					.ThenBy(a => a.Name, StringComparer.Ordinal)
					.ToList();
			}

			return visibleTools
				.ToList();
		}

		internal bool IsToolVisible(string internalToolName, string? sessionId) {
			if (Configuration.McpConfig.Instance.ExposeFullToolCatalog)
				return true;
			if (ToolCatalogMetadata.IsBootstrapTool(internalToolName))
				return true;
			if (!ToolCatalogMetadata.IsDiscoverable(internalToolName))
				return false;

			var effectiveSessionId = NormalizeSessionId(sessionId);
			if (string.IsNullOrEmpty(effectiveSessionId))
				return false;

			var group = ToolCatalogMetadata.GetGroup(internalToolName);
			lock (enabledToolGroupsLock) {
				return enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var enabledGroups) &&
				       enabledGroups.Contains(group);
			}
		}

		internal CallToolResult CreateHiddenToolError(string publicToolName) {
			if (!ToolNameMapper.TryMapPublicToInternal(publicToolName, out var internalToolName))
				return ToolResponseFactory.Text($"Unknown tool: {publicToolName}", true, new {
					tool_name = publicToolName,
					error = "unknown_tool"
				});

			var group = ToolCatalogMetadata.GetGroup(internalToolName);
			return ToolResponseFactory.Json(new {
				error = "tool_not_enabled",
				tool_name = publicToolName,
				required_group = group,
				message = $"{publicToolName} is hidden until its tool group is enabled for this session.",
				next_steps = new[] {
					$"Call dnspy_enable_tool_groups with groups=[\"{group}\"]",
					$"Or use dnspy_search_tools + dnspy_get_tool_schemas + dnspy_execute_code for one-off composed workflows"
				}
			}, true);
		}

		internal CallToolResult HandleSearchTools(Dictionary<string, object>? arguments, string? sessionId) {
			var query = GetOptionalString(arguments, "query");
			var detail = NormalizeDetailLevel(GetOptionalString(arguments, "detail"), "brief");
			var groupFilter = GetOptionalString(arguments, "group");
			var limit = Math.Max(1, Math.Min(100, GetOptionalInt(arguments, "limit", 12)));
			var queryTokens = Tokenize(query);
			var catalog = GetToolCatalog().Tools
				.Where(a => ToolNameMapper.TryMapPublicToInternal(a.Name, out var internalName) && ToolCatalogMetadata.IsDiscoverable(internalName));

			if (!string.IsNullOrWhiteSpace(groupFilter))
				catalog = catalog.Where(a => string.Equals(GetGroupForPublicTool(a.Name), groupFilter, StringComparison.OrdinalIgnoreCase));

			var items = catalog
				.Select(a => new {
					Tool = a,
					Score = ScoreTool(a, query ?? string.Empty, queryTokens)
				})
				.Where(a => string.IsNullOrWhiteSpace(query) || a.Score > 0)
				.OrderByDescending(a => a.Score)
				.ThenBy(a => a.Tool.Name, StringComparer.Ordinal)
				.Take(limit)
				.Select(a => BuildToolDescriptor(a.Tool, detail, sessionId, includeSchemas: detail == "full"))
				.ToList();

			return ToolResponseFactory.Json(new {
				query = query ?? string.Empty,
				detail,
				group = groupFilter,
				returned_count = items.Count,
				items
			});
		}

		internal CallToolResult HandleGetToolSchemas(Dictionary<string, object>? arguments, string? sessionId) {
			var detail = NormalizeDetailLevel(GetOptionalString(arguments, "detail"), "full");
			var toolNames = GetStringList(arguments, "tool_names");
			if (toolNames.Count == 0)
				throw new ArgumentException("'tool_names' must contain at least one tool name.");

			var catalog = GetToolCatalog().Tools.ToDictionary(a => a.Name, StringComparer.Ordinal);
			var items = new List<object>();
			var missing = new List<string>();

			foreach (var toolName in toolNames.Distinct(StringComparer.Ordinal)) {
				if (!catalog.TryGetValue(toolName, out var tool)) {
					missing.Add(toolName);
					continue;
				}

				if (!ToolNameMapper.TryMapPublicToInternal(tool.Name, out var internalToolName) || !ToolCatalogMetadata.IsDiscoverable(internalToolName)) {
					missing.Add(toolName);
					continue;
				}

				items.Add(BuildToolDescriptor(tool, detail, sessionId, includeSchemas: true));
			}

			return ToolResponseFactory.Json(new {
				detail,
				returned_count = items.Count,
				missing,
				items
			});
		}

		internal CallToolResult HandleListToolGroups(string? sessionId) {
			var catalog = GetToolCatalog().Tools
				.Where(a => ToolNameMapper.TryMapPublicToInternal(a.Name, out var internalName) && ToolCatalogMetadata.IsDiscoverable(internalName))
				.ToList();

			var groups = ToolCatalogMetadata.AllGroups
				.Select(group => {
					var groupTools = catalog
						.Where(a => string.Equals(GetGroupForPublicTool(a.Name), group, StringComparison.Ordinal))
						.OrderBy(a => a.Name, StringComparer.Ordinal)
						.ToList();
					return new {
						group,
						description = toolGroupDescriptions.TryGetValue(group, out var description) ? description : group,
						tool_count = groupTools.Count,
						visible = group == "bootstrap" || IsGroupEnabledForSession(group, sessionId),
						representative_tools = groupTools.Take(5).Select(a => a.Name).ToList()
					};
				})
				.Where(a => a.tool_count > 0 || a.group == "bootstrap")
				.ToList();

			return ToolResponseFactory.Json(new {
				returned_count = groups.Count,
				items = groups
			});
		}

		internal CallToolResult HandleEnableToolGroups(Dictionary<string, object>? arguments, string? sessionId) {
			var effectiveSessionId = ResolveEffectiveSessionId(sessionId, arguments);
			if (string.IsNullOrEmpty(effectiveSessionId))
				throw new ArgumentException("A live session is required. Use the SSE /message sessionId or pass session_id explicitly.");

			var groups = NormalizeGroups(GetStringList(arguments, "groups"));
			if (groups.Count == 0)
				throw new ArgumentException("'groups' must contain at least one tool group.");

			lock (enabledToolGroupsLock) {
				if (!enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var enabledGroups)) {
					enabledGroups = new HashSet<string>(StringComparer.Ordinal);
					enabledToolGroupsBySession[effectiveSessionId] = enabledGroups;
				}

				foreach (var group in groups)
					enabledGroups.Add(group);
			}

			return BuildEnabledGroupsResult(effectiveSessionId);
		}

		internal CallToolResult HandleDisableToolGroups(Dictionary<string, object>? arguments, string? sessionId) {
			var effectiveSessionId = ResolveEffectiveSessionId(sessionId, arguments);
			if (string.IsNullOrEmpty(effectiveSessionId))
				throw new ArgumentException("A live session is required. Use the SSE /message sessionId or pass session_id explicitly.");

			var groups = NormalizeGroups(GetStringList(arguments, "groups"));
			if (groups.Count == 0)
				throw new ArgumentException("'groups' must contain at least one tool group.");

			lock (enabledToolGroupsLock) {
				if (enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var enabledGroups)) {
					foreach (var group in groups)
						enabledGroups.Remove(group);
					if (enabledGroups.Count == 0)
						enabledToolGroupsBySession.Remove(effectiveSessionId);
				}
			}

			return BuildEnabledGroupsResult(effectiveSessionId);
		}

		internal CallToolResult HandleGetEnabledToolGroups(Dictionary<string, object>? arguments, string? sessionId) {
			var effectiveSessionId = ResolveEffectiveSessionId(sessionId, arguments);
			return BuildEnabledGroupsResult(effectiveSessionId);
		}

		internal string? ResolveEffectiveSessionId(string? sessionId, Dictionary<string, object>? arguments = null) {
			var explicitSessionId = GetOptionalString(arguments, "session_id");
			return NormalizeSessionId(!string.IsNullOrWhiteSpace(explicitSessionId) ? explicitSessionId : sessionId);
		}

		internal bool IsToolVisibleByPublicName(string publicToolName, string? sessionId) {
			if (!ToolNameMapper.TryMapPublicToInternal(publicToolName, out var internalToolName))
				return false;
			return IsToolVisible(internalToolName, sessionId);
		}

		internal bool IsGroupEnabledForSession(string group, string? sessionId) {
			if (group == "bootstrap")
				return true;

			var effectiveSessionId = NormalizeSessionId(sessionId);
			if (string.IsNullOrEmpty(effectiveSessionId))
				return false;

			lock (enabledToolGroupsLock) {
				return enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var enabledGroups) &&
				       enabledGroups.Contains(group);
			}
		}

		int GetInitialCodeModeBootstrapOrder(string publicToolName) {
			for (int i = 0; i < initialCodeModeBootstrapToolOrder.Length; i++) {
				if (string.Equals(initialCodeModeBootstrapToolOrder[i], publicToolName, StringComparison.Ordinal))
					return i;
			}

			return int.MaxValue;
		}

		internal bool HasAnyWorkflowGroupsEnabled(string? sessionId) {
			var effectiveSessionId = NormalizeSessionId(sessionId);
			if (string.IsNullOrEmpty(effectiveSessionId))
				return false;

			lock (enabledToolGroupsLock) {
				return enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var enabledGroups) &&
				       enabledGroups.Any();
			}
		}

		static string? NormalizeSessionId(string? sessionId) {
			if (!string.IsNullOrWhiteSpace(sessionId))
				return sessionId.Trim();

			var config = Configuration.McpConfig.Instance;
			if (!config.AllowImplicitDefaultSession)
				return null;

			return string.IsNullOrWhiteSpace(config.ImplicitDefaultSessionId)
				? "__implicit_http_session__"
				: config.ImplicitDefaultSessionId.Trim();
		}

		static string NormalizeDetailLevel(string? detail, string defaultValue) {
			var normalized = string.IsNullOrWhiteSpace(detail) ? defaultValue : detail.Trim().ToLowerInvariant();
			return normalized switch {
				"brief" or "detailed" or "full" => normalized,
				_ => defaultValue
			};
		}

		static string? GetOptionalString(Dictionary<string, object>? arguments, string name) {
			if (arguments == null || !arguments.TryGetValue(name, out var value) || value == null)
				return null;

			if (value is JsonElement element) {
				if (element.ValueKind == JsonValueKind.String)
					return element.GetString();
				if (element.ValueKind == JsonValueKind.Number || element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
					return element.ToString();
				return null;
			}

			return value.ToString();
		}

		static int GetOptionalInt(Dictionary<string, object>? arguments, string name, int defaultValue) {
			if (arguments == null || !arguments.TryGetValue(name, out var value) || value == null)
				return defaultValue;
			if (value is JsonElement element) {
				if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericValue))
					return numericValue;
				if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var stringValue))
					return stringValue;
			}
			return int.TryParse(value.ToString(), out var parsedValue) ? parsedValue : defaultValue;
		}

		static List<string> GetStringList(Dictionary<string, object>? arguments, string name) {
			if (arguments == null || !arguments.TryGetValue(name, out var value) || value == null)
				return new List<string>();

			if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
				return element.EnumerateArray()
					.Where(a => a.ValueKind == JsonValueKind.String)
					.Select(a => a.GetString())
					.Where(a => !string.IsNullOrWhiteSpace(a))
					.Select(a => a!)
					.ToList();

			if (value is IEnumerable<object> objectEnumerable)
				return objectEnumerable
					.Select(a => a?.ToString())
					.Where(a => !string.IsNullOrWhiteSpace(a))
					.Select(a => a!)
					.ToList();

			var single = value.ToString();
			return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single };
		}

		List<string> NormalizeGroups(List<string> groups) {
			var normalizedGroups = new List<string>();
			foreach (var group in groups) {
				var normalized = group?.Trim();
				if (string.IsNullOrWhiteSpace(normalized))
					continue;
				if (!ToolCatalogMetadata.AllGroups.Contains(normalized, StringComparer.Ordinal))
					throw new ArgumentException($"Unknown tool group: {normalized}");
				if (normalized == "bootstrap")
					continue;
				normalizedGroups.Add(normalized);
			}
			return normalizedGroups.Distinct(StringComparer.Ordinal).ToList();
		}

		CallToolResult BuildEnabledGroupsResult(string? sessionId) {
			var effectiveSessionId = NormalizeSessionId(sessionId);
			var enabledGroups = new List<string>();
			var config = Configuration.McpConfig.Instance;
			lock (enabledToolGroupsLock) {
				if (!string.IsNullOrEmpty(effectiveSessionId) &&
				    enabledToolGroupsBySession.TryGetValue(effectiveSessionId, out var groups))
					enabledGroups = groups.OrderBy(a => a, StringComparer.Ordinal).ToList();
			}

			var visibleTools = GetAvailableTools(effectiveSessionId)
				.Select(a => a.Name)
				.OrderBy(a => a, StringComparer.Ordinal)
				.ToList();

			return ToolResponseFactory.Json(new {
				session_id = effectiveSessionId,
				session_mode = string.Equals(effectiveSessionId, config.ImplicitDefaultSessionId, StringComparison.Ordinal)
					? "implicit_default"
					: "explicit",
				enabled_groups = enabledGroups,
				preferred_bootstrap = enabledGroups.Count == 0 ? "code_mode_first" : "direct_tools_enabled",
				visible_tools = visibleTools,
				recommended_next_steps = enabledGroups.Count == 0
					? new[] {
						"Start with dnspy_execute_code for multi-step analysis and let code mode call hidden dnSpy tools as needed.",
						"Only call dnspy_enable_tool_groups if you want direct tools/list exposure for a workflow area."
					}
					: new[] {
						"Use tools/list for directly enabled workflow tools.",
						"Use dnspy_execute_code when you need to compose many dnSpy tools in one step."
					},
				hidden_discoverable_groups = ToolCatalogMetadata.AllGroups
					.Where(a => a != "bootstrap" && !enabledGroups.Contains(a))
					.ToList()
			});
		}

		object BuildToolDescriptor(ToolInfo tool, string detail, string? sessionId, bool includeSchemas) {
			var group = GetGroupForPublicTool(tool.Name);
			var descriptor = new Dictionary<string, object> {
				["name"] = tool.Name,
				["description"] = tool.Description,
				["group"] = group,
				["tags"] = GetTagsForPublicTool(tool.Name),
				["visible"] = IsToolVisibleByPublicName(tool.Name, sessionId)
			};

			if (detail == "detailed" || detail == "full") {
				var required = TryGetRequiredProperties(tool.InputSchema);
				descriptor["required"] = required;
				descriptor["parameters"] = GetSchemaPropertySummaries(tool.InputSchema);
			}

			if (includeSchemas) {
				descriptor["input_schema"] = tool.InputSchema;
				if (tool.OutputSchema != null)
					descriptor["output_schema"] = tool.OutputSchema;
				if (tool.Annotations != null)
					descriptor["annotations"] = tool.Annotations;
			}

			return descriptor;
		}

		static List<string> TryGetRequiredProperties(Dictionary<string, object> schema) {
			if (schema.TryGetValue("required", out var requiredObj) && requiredObj is List<string> required)
				return required;
			return new List<string>();
		}

		static List<object> GetSchemaPropertySummaries(Dictionary<string, object> schema) {
			if (!schema.TryGetValue("properties", out var propertiesObj) || propertiesObj is not Dictionary<string, object> properties)
				return new List<object>();

			return properties
				.OrderBy(a => a.Key, StringComparer.Ordinal)
				.Select(a => {
					var propertySchema = a.Value as Dictionary<string, object>;
					var propertyType = propertySchema != null && propertySchema.TryGetValue("type", out var typeObj) ? typeObj?.ToString() ?? "object" : "object";
					var description = propertySchema != null && propertySchema.TryGetValue("description", out var descriptionObj) ? descriptionObj?.ToString() ?? string.Empty : string.Empty;
					return (object)new Dictionary<string, object> {
						["name"] = a.Key,
						["type"] = propertyType,
						["description"] = description
					};
				})
				.ToList();
		}

		int ScoreTool(ToolInfo tool, string query, IReadOnlyList<string> queryTokens) {
			if (string.IsNullOrWhiteSpace(query))
				return 1;

			var lowerQuery = query.Trim().ToLowerInvariant();
			var name = tool.Name.ToLowerInvariant();
			var description = tool.Description.ToLowerInvariant();
			var group = GetGroupForPublicTool(tool.Name).ToLowerInvariant();
			var tags = GetTagsForPublicTool(tool.Name).Select(a => a.ToLowerInvariant()).ToList();
			var score = 0;

			if (name == lowerQuery)
				score += 1000;
			else if (name.Contains(lowerQuery))
				score += 400;

			if (group.Contains(lowerQuery))
				score += 250;

			foreach (var token in queryTokens) {
				if (name.Contains(token))
					score += 120;
				if (description.Contains(token))
					score += 45;
				if (group.Contains(token))
					score += 80;
				score += tags.Count(a => a.Contains(token)) * 60;
			}

			return score;
		}

		static IReadOnlyList<string> Tokenize(string? text) {
			if (string.IsNullOrWhiteSpace(text))
				return Array.Empty<string>();

			return text.Split(new[] { ' ', '\t', '\r', '\n', '-', '_', '/', '\\', ':', '.' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(a => a.Trim().ToLowerInvariant())
				.Where(a => a.Length > 1)
				.Distinct(StringComparer.Ordinal)
				.ToList();
		}

		string GetGroupForPublicTool(string publicToolName) {
			return ToolNameMapper.TryMapPublicToInternal(publicToolName, out var internalToolName)
				? ToolCatalogMetadata.GetGroup(internalToolName)
				: "legacy_compat";
		}

		IReadOnlyList<string> GetTagsForPublicTool(string publicToolName) {
			return ToolNameMapper.TryMapPublicToInternal(publicToolName, out var internalToolName)
				? ToolCatalogMetadata.GetTags(internalToolName)
				: Array.Empty<string>();
		}
	}
}
