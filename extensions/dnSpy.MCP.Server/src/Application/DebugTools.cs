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
using System.Linq;
using System.Text.Json;
using System.Threading;
using dnlib.DotNet;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.Attach;
using dnSpy.Contracts.Debugger.Breakpoints.Code;
using dnSpy.Contracts.Debugger.CallStack;
using dnSpy.Contracts.Debugger.DotNet.Breakpoints.Code;
using dnSpy.Contracts.Debugger.DotNet.CorDebug;
using dnSpy.Contracts.Debugger.Exceptions;
using dnSpy.Contracts.Debugger.Steppers;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Metadata;
using dnSpy.Contracts.TreeView;
using dnSpy.Contracts.ToolWindows.App;
using dnSpy.MCP.Server.Contracts;
using dnSpy.MCP.Server.Helper;

namespace dnSpy.MCP.Server.Application {
	/// <summary>
	/// Debugger integration tools: get state, manage breakpoints, control execution, and inspect the call stack.
	/// Requires the Debugger extension to be loaded. Operations that need a paused debugger will return
	/// descriptive errors when the debugger is not in the required state.
	/// </summary>
	[Export(typeof(DebugTools))]
	public sealed class DebugTools {
		readonly Lazy<DbgManager> dbgManager;
		readonly Lazy<DbgCodeBreakpointsService> breakpointsService;
		readonly Lazy<DbgDotNetBreakpointFactory> breakpointFactory;
		readonly IDocumentTreeView documentTreeView;
		readonly Lazy<IDocumentTabService> documentTabService;
		readonly Lazy<AttachableProcessesService> attachableProcessesService;
		readonly Lazy<DbgExceptionSettingsService> exceptionSettingsService;
		readonly Lazy<IDsToolWindowService> toolWindowService;

		[ImportingConstructor]
		public DebugTools(
			Lazy<DbgManager> dbgManager,
			Lazy<DbgCodeBreakpointsService> breakpointsService,
			Lazy<DbgDotNetBreakpointFactory> breakpointFactory,
			IDocumentTreeView documentTreeView,
			Lazy<IDocumentTabService> documentTabService,
			Lazy<AttachableProcessesService> attachableProcessesService,
			Lazy<DbgExceptionSettingsService> exceptionSettingsService,
			Lazy<IDsToolWindowService> toolWindowService) {
			this.dbgManager = dbgManager;
			this.breakpointsService = breakpointsService;
			this.breakpointFactory = breakpointFactory;
			this.documentTreeView = documentTreeView;
			this.documentTabService = documentTabService;
			this.attachableProcessesService = attachableProcessesService;
			this.exceptionSettingsService = exceptionSettingsService;
			this.toolWindowService = toolWindowService;
		}

		/// <summary>
		/// Returns the current debugger state.
		/// Arguments: none
		/// </summary>
		public CallToolResult GetDebuggerState() {
			try {
				var mgr = dbgManager.Value;
				var processes = mgr.Processes.Select(p => new {
					Id = p.Id,
					State = p.State.ToString(),
					IsRunning = p.IsRunning,
					RuntimeCount = p.Runtimes.Length,
					ThreadCount = p.Threads.Length
				}).ToList();

				var result = JsonSerializer.Serialize(new {
					IsDebugging = mgr.IsDebugging,
					IsRunning = mgr.IsRunning,
					ProcessCount = processes.Count,
					Processes = processes
				}, new JsonSerializerOptions { WriteIndented = true });

				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "GetDebuggerState failed");
				var result = JsonSerializer.Serialize(new {
					IsDebugging = false,
					IsRunning = (bool?)null,
					ProcessCount = 0,
					Processes = Array.Empty<object>(),
					Error = ex.Message
				}, new JsonSerializerOptions { WriteIndented = true });
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
		}

		/// <summary>
		/// Lists all code breakpoints currently registered in dnSpy.
		/// Arguments: none
		/// </summary>
		public CallToolResult ListBreakpoints() {
			try {
				var service = breakpointsService.Value;
				var bps = service.VisibleBreakpoints.Select((bp, idx) => new {
					Index = idx,
					Id = bp.Id,
					IsEnabled = bp.IsEnabled,
					IsHidden = bp.IsHidden,
					BoundCount = bp.BoundBreakpoints.Length,
					LocationType = bp.Location?.Type ?? "Unknown",
					LocationString = bp.Location?.ToString() ?? "Unknown",
					Labels = bp.Labels?.ToArray() ?? Array.Empty<string>()
				}).ToList();

				var result = JsonSerializer.Serialize(new {
					BreakpointCount = bps.Count,
					Breakpoints = bps
				}, new JsonSerializerOptions { WriteIndented = true });

				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "ListBreakpoints failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error listing breakpoints: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Returns detailed information about a visible breakpoint.
		/// Arguments: index (optional) | breakpoint_id (optional)
		/// </summary>
		public CallToolResult InspectBreakpoint(Dictionary<string, object>? arguments) {
			try {
				var visibleBreakpoints = breakpointsService.Value.VisibleBreakpoints.ToList();
				if (visibleBreakpoints.Count == 0) {
					return new CallToolResult {
						Content = new List<ToolContent> { new ToolContent { Text = "No visible breakpoints." } }
					};
				}

				var breakpointId = GetOptionalInt(arguments, "breakpoint_id");
				var index = GetOptionalInt(arguments, "index");

				DbgCodeBreakpoint? breakpoint = null;
				int resolvedIndex = -1;

				if (breakpointId is int id) {
					breakpoint = visibleBreakpoints.FirstOrDefault(a => a.Id == id);
					if (breakpoint == null)
						throw new ArgumentException($"No visible breakpoint found with id {id}.");
					resolvedIndex = visibleBreakpoints.IndexOf(breakpoint);
				}
				else if (index is int idx) {
					if (idx < 0 || idx >= visibleBreakpoints.Count)
						throw new ArgumentException($"Breakpoint index {idx} is out of range. Visible breakpoints: {visibleBreakpoints.Count}.");
					breakpoint = visibleBreakpoints[idx];
					resolvedIndex = idx;
				}
				else {
					breakpoint = visibleBreakpoints[0];
					resolvedIndex = 0;
				}

				var payload = new {
					SelectedIndex = resolvedIndex,
					Breakpoint = DescribeBreakpoint(breakpoint),
					VisibleBreakpointCount = visibleBreakpoints.Count
				};

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "InspectBreakpoint failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error inspecting breakpoint: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Returns information about the currently selected document-tree node.
		/// Arguments: none
		/// </summary>
		public CallToolResult GetSelectedNode() {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					var selectedNode = documentTreeView.TreeView.SelectedItem as DocumentTreeNodeData;
					return new {
						HasSelection = selectedNode != null,
						Node = selectedNode != null ? DescribeNode(selectedNode) : null
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "GetSelectedNode failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error getting selected node: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Returns information about the active document tab.
		/// Arguments: none
		/// </summary>
		public CallToolResult GetActiveTab() {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					var activeTab = documentTabService.Value.ActiveTab;
					return new {
						HasActiveTab = activeTab != null,
						Tab = activeTab != null ? DescribeTab(activeTab) : null
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "GetActiveTab failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error getting active tab: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Selects a document-tree node corresponding to an assembly/type/member/token and optionally opens it in a tab.
		/// </summary>
		public CallToolResult SelectDocumentNode(Dictionary<string, object>? arguments) {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					var reference = ResolveReference(arguments);
					var node = FindNodeForReference(reference);
					if (node == null)
						throw new InvalidOperationException("dnSpy could not locate a document-tree node for the requested reference.");

					bool openInTab = GetOptionalBool(arguments, "open_in_tab", true);
					bool newTab = GetOptionalBool(arguments, "new_tab", false);
					bool setFocus = GetOptionalBool(arguments, "set_focus", true);

					documentTreeView.TreeView.SelectItems(new[] { node });
					documentTreeView.TreeView.ScrollIntoView();
					if (setFocus)
						documentTreeView.TreeView.Focus();

					bool openedTab = false;
					if (openInTab) {
						documentTabService.Value.FollowReference(reference, newTab, setFocus);
						openedTab = true;
					}

					return new {
						Selected = true,
						OpenedTab = openedTab,
						Node = DescribeNode(node),
						Reference = DescribeReference(reference),
						ActiveTab = documentTabService.Value.ActiveTab != null ? DescribeTab(documentTabService.Value.ActiveTab!) : null
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "SelectDocumentNode failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error selecting document node: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Follows a reference in dnSpy, either from the active viewer selection or from explicit symbol arguments.
		/// </summary>
		public CallToolResult FollowReference(Dictionary<string, object>? arguments) {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					var service = documentTabService.Value;
					var activeTab = service.ActiveTab;
					if (activeTab == null)
						throw new InvalidOperationException("No active document tab is available.");

					bool newTab = GetOptionalBool(arguments, "new_tab", false);
					bool setFocus = GetOptionalBool(arguments, "set_focus", true);
					bool useSelectedReference = arguments == null || arguments.Count == 0 || GetOptionalBool(arguments, "use_selected_reference", false);

					object reference;
					string source;
					if (useSelectedReference) {
						var viewer = activeTab.TryGetDocumentViewer();
						var selectedReference = viewer?.SelectedReference;
						if (selectedReference == null || selectedReference.Value.Data.Reference == null)
							throw new InvalidOperationException("The active tab does not have a selected reference at the caret. Provide explicit symbol arguments to follow a reference directly.");
						reference = selectedReference.Value.Data.Reference;
						source = "active_tab_selected_reference";
					}
					else {
						reference = ResolveReference(arguments);
						source = "resolved_arguments";
					}

					ShowTabContentResult? followResult = null;
					service.FollowReference(reference, newTab, setFocus, e => followResult = e.Result);

					return new {
						Source = source,
						NewTab = newTab,
						SetFocus = setFocus,
						FollowResult = followResult?.ToString(),
						Reference = DescribeReference(reference),
						ActiveTab = service.ActiveTab != null ? DescribeTab(service.ActiveTab!) : null
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "FollowReference failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error following reference: {ex.Message}" } },
					IsError = true
				};
			}
		}

		public CallToolResult GetToolWindowState(Dictionary<string, object>? arguments) {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					var requestedContext = arguments != null && arguments.TryGetValue("context", out var contextObj)
						? contextObj?.ToString()
						: null;

					var windows = GetKnownToolWindows()
						.Where(a => string.IsNullOrWhiteSpace(requestedContext) || string.Equals(a.Context, requestedContext, StringComparison.OrdinalIgnoreCase))
						.Select(a => new {
							Context = a.Context,
							Title = a.Title,
							IsShown = toolWindowService.Value.IsShown(a.Guid),
							RequiresDebugging = a.RequiresDebugging,
							Guid = a.Guid
						})
						.ToList();

					if (!string.IsNullOrWhiteSpace(requestedContext) && windows.Count == 0)
						throw new ArgumentException($"Unknown tool window context: {requestedContext}");

					return new {
						IsDebugging = dbgManager.Value.IsDebugging,
						Count = windows.Count,
						Windows = windows
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "GetToolWindowState failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error getting tool window state: {ex.Message}" } },
					IsError = true
				};
			}
		}

		public CallToolResult FocusDebuggerContext(Dictionary<string, object>? arguments) {
			try {
				var payload = UiThreadHelper.Invoke(() => {
					if (arguments == null || !arguments.TryGetValue("context", out var contextObj) || string.IsNullOrWhiteSpace(contextObj?.ToString()))
						throw new ArgumentException("context is required");

					var descriptor = ResolveKnownToolWindow(contextObj!.ToString()!);
					if (descriptor.RequiresDebugging && !dbgManager.Value.IsDebugging)
						throw new InvalidOperationException($"The '{descriptor.Context}' tool window requires an active debug session.");

					var content = toolWindowService.Value.Show(descriptor.Guid);
					return new {
						Context = descriptor.Context,
						Title = descriptor.Title,
						WasShown = content != null,
						IsShown = toolWindowService.Value.IsShown(descriptor.Guid),
						Guid = descriptor.Guid
					};
				});

				return new CallToolResult {
					Content = new List<ToolContent> {
						new ToolContent { Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) }
					}
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "FocusDebuggerContext failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error focusing debugger context: {ex.Message}" } },
					IsError = true
				};
			}
		}

		/// <summary>
		/// Sets a breakpoint at a method entry point (IL offset 0 by default).
		/// Arguments: assembly_name, type_full_name, method_name, il_offset (optional, default 0)
		/// The breakpoint persists across debug sessions via dnSpy's breakpoint storage.
		/// </summary>
		public CallToolResult SetBreakpoint(Dictionary<string, object>? arguments) {
			if (arguments == null)
				throw new ArgumentException("Arguments required");
			if (!arguments.TryGetValue("assembly_name", out var asmNameObj))
				throw new ArgumentException("assembly_name is required");
			if (!arguments.TryGetValue("type_full_name", out var typeNameObj))
				throw new ArgumentException("type_full_name is required");
			if (!arguments.TryGetValue("method_name", out var methodNameObj))
				throw new ArgumentException("method_name is required");

			string? filePath = null;
			if (arguments.TryGetValue("file_path", out var fpObj))
				filePath = fpObj?.ToString();

			var assembly = FindAssemblyByName(asmNameObj.ToString() ?? "", filePath);
			if (assembly == null)
				throw new ArgumentException($"Assembly not found: {asmNameObj}");

			var type = FindTypeInAssembly(assembly, typeNameObj.ToString() ?? "");
			if (type == null)
				throw new ArgumentException($"Type not found: {typeNameObj}");

			var method = type.Methods.FirstOrDefault(m => m.Name.String == (methodNameObj.ToString() ?? ""));
			if (method == null)
				throw new ArgumentException($"Method not found: {methodNameObj}");

			uint ilOffset = 0;
			if (arguments.TryGetValue("il_offset", out var offsetObj) && offsetObj is JsonElement offsetElem)
				offsetElem.TryGetUInt32(out ilOffset);

			var module = method.Module;
			var moduleId = ModuleId.CreateFromFile(module);
			var token = method.MDToken.Raw;

			// Optional condition expression
			string? conditionExpr = null;
			if (arguments.TryGetValue("condition", out var condObj) && !string.IsNullOrWhiteSpace(condObj?.ToString()))
				conditionExpr = condObj!.ToString()!.Trim();

			try {
				var bp = breakpointFactory.Value.Create(moduleId, token, ilOffset);
				if (bp == null) {
					return new CallToolResult {
						Content = new List<ToolContent> { new ToolContent { Text = $"A breakpoint already exists at {method.FullName} +IL_{ilOffset:X4}" } }
					};
				}

				// Apply condition if provided
				if (conditionExpr != null)
					bp.Condition = new DbgCodeBreakpointCondition(DbgCodeBreakpointConditionKind.IsTrue, conditionExpr);

				var result = JsonSerializer.Serialize(new {
					Success    = true,
					Method     = method.FullName,
					ILOffset   = ilOffset,
					Token      = $"0x{token:X8}",
					ModulePath = module.Location,
					IsEnabled  = bp.IsEnabled,
					Condition  = conditionExpr
				}, new JsonSerializerOptions {
					WriteIndented = true,
					DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
				});

				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "SetBreakpoint failed");
				throw new Exception($"Failed to set breakpoint at {method.FullName}: {ex.Message}");
			}
		}

		public CallToolResult SetTracepoint(Dictionary<string, object>? arguments) {
			if (arguments == null)
				throw new ArgumentException("Arguments required");
			if (!arguments.TryGetValue("assembly_name", out var asmNameObj))
				throw new ArgumentException("assembly_name is required");
			if (!arguments.TryGetValue("type_full_name", out var typeNameObj))
				throw new ArgumentException("type_full_name is required");
			if (!arguments.TryGetValue("method_name", out var methodNameObj))
				throw new ArgumentException("method_name is required");

			string? filePath = null;
			if (arguments.TryGetValue("file_path", out var fpObj))
				filePath = fpObj?.ToString();

			var assembly = FindAssemblyByName(asmNameObj.ToString() ?? "", filePath);
			if (assembly == null)
				throw new ArgumentException($"Assembly not found: {asmNameObj}");

			var type = FindTypeInAssembly(assembly, typeNameObj.ToString() ?? "");
			if (type == null)
				throw new ArgumentException($"Type not found: {typeNameObj}");

			var method = type.Methods.FirstOrDefault(m => m.Name.String == (methodNameObj.ToString() ?? ""));
			if (method == null)
				throw new ArgumentException($"Method not found: {methodNameObj}");

			uint ilOffset = 0;
			if (arguments.TryGetValue("il_offset", out var offsetObj) && offsetObj is JsonElement offsetElem)
				offsetElem.TryGetUInt32(out ilOffset);

			string? message = null;
			if (arguments.TryGetValue("message", out var messageObj) && !string.IsNullOrWhiteSpace(messageObj?.ToString()))
				message = messageObj!.ToString();

			bool continueExecution = true;
			if (arguments.TryGetValue("continue_execution", out var continueObj))
				continueExecution = ReadBool(continueObj, true);

			var module = method.Module;
			var moduleId = ModuleId.CreateFromFile(module);
			var token = method.MDToken.Raw;

			try {
				var settings = new DbgCodeBreakpointSettings {
					IsEnabled = true,
					Trace = new DbgCodeBreakpointTrace(message ?? string.Empty, continueExecution)
				};
				var bp = breakpointFactory.Value.Create(moduleId, token, ilOffset, settings);
				if (bp == null) {
					return new CallToolResult {
						Content = new List<ToolContent> { new ToolContent { Text = $"A breakpoint or tracepoint already exists at {method.FullName} +IL_{ilOffset:X4}" } }
					};
				}

				var result = JsonSerializer.Serialize(new {
					Success = true,
					Method = method.FullName,
					ILOffset = ilOffset,
					Token = $"0x{token:X8}",
					ModulePath = module.Location,
					IsEnabled = bp.IsEnabled,
					TraceMessage = message ?? string.Empty,
					ContinueExecution = continueExecution
				}, new JsonSerializerOptions {
					WriteIndented = true,
					DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
				});

				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "SetTracepoint failed");
				throw new Exception($"Failed to set tracepoint at {method.FullName}: {ex.Message}");
			}
		}

		/// <summary>
		/// Removes a breakpoint from a method.
		/// Arguments: assembly_name, type_full_name, method_name, il_offset (optional, default 0)
		/// </summary>
		public CallToolResult RemoveBreakpoint(Dictionary<string, object>? arguments) {
			if (arguments == null)
				throw new ArgumentException("Arguments required");
			if (!arguments.TryGetValue("assembly_name", out var asmNameObj))
				throw new ArgumentException("assembly_name is required");
			if (!arguments.TryGetValue("type_full_name", out var typeNameObj))
				throw new ArgumentException("type_full_name is required");
			if (!arguments.TryGetValue("method_name", out var methodNameObj))
				throw new ArgumentException("method_name is required");

			var assembly = FindAssemblyByName(asmNameObj.ToString() ?? "");
			if (assembly == null)
				throw new ArgumentException($"Assembly not found: {asmNameObj}");

			var type = FindTypeInAssembly(assembly, typeNameObj.ToString() ?? "");
			if (type == null)
				throw new ArgumentException($"Type not found: {typeNameObj}");

			var method = type.Methods.FirstOrDefault(m => m.Name.String == (methodNameObj.ToString() ?? ""));
			if (method == null)
				throw new ArgumentException($"Method not found: {methodNameObj}");

			uint ilOffset = 0;
			if (arguments.TryGetValue("il_offset", out var offsetObj) && offsetObj is JsonElement offsetElem)
				offsetElem.TryGetUInt32(out ilOffset);

			var module = method.Module;
			var moduleId = ModuleId.CreateFromFile(module);
			var token = method.MDToken.Raw;

			var bp = breakpointFactory.Value.TryGetBreakpoint(moduleId, token, ilOffset);
			if (bp == null) {
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"No breakpoint found at {method.FullName} +IL_{ilOffset:X4}" } }
				};
			}

			breakpointsService.Value.Remove(bp);

			return new CallToolResult {
				Content = new List<ToolContent> { new ToolContent { Text = $"Breakpoint removed from {method.FullName} +IL_{ilOffset:X4}" } }
			};
		}

		/// <summary>
		/// Removes all visible breakpoints.
		/// Arguments: none
		/// </summary>
		public CallToolResult ClearAllBreakpoints() {
			try {
				breakpointsService.Value.Clear();
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = "All breakpoints cleared." } }
				};
			}
			catch (Exception ex) {
				throw new Exception($"Failed to clear breakpoints: {ex.Message}");
			}
		}

		/// <summary>
		/// Resumes execution of all paused processes.
		/// Arguments: none
		/// </summary>
		public CallToolResult ContinueDebugger() {
			try {
				dbgManager.Value.RunAll();
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = "Debugger resumed (RunAll called)." } }
				};
			}
			catch (Exception ex) {
				throw new Exception($"Failed to continue debugger: {ex.Message}");
			}
		}

		/// <summary>
		/// Pauses all running processes.
		/// Arguments: none
		/// </summary>
		public CallToolResult BreakDebugger() {
			try {
				dbgManager.Value.BreakAll();
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = "Debugger paused (BreakAll called)." } }
				};
			}
			catch (Exception ex) {
				throw new Exception($"Failed to break debugger: {ex.Message}");
			}
		}

		/// <summary>
		/// Stops all active debug sessions.
		/// Arguments: none
		/// </summary>
		public CallToolResult StopDebugging() {
			try {
				dbgManager.Value.StopDebuggingAll();
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = "All debug sessions stopped." } }
				};
			}
			catch (Exception ex) {
				throw new Exception($"Failed to stop debugging: {ex.Message}");
			}
		}

		/// <summary>
		/// Returns the call stack of the current (or first paused) thread.
		/// Arguments: none — debugger must be paused.
		/// </summary>
		public CallToolResult GetCallStack() {
			try {
				var mgr = dbgManager.Value;

				if (!mgr.IsDebugging) {
					return new CallToolResult {
						Content = new List<ToolContent> { new ToolContent { Text = "Debugger is not active. Start debugging first." } }
					};
				}

				// Prefer the currently selected thread; fall back to the first paused thread.
				DbgThread? currentThread = mgr.CurrentThread?.Current;

				if (currentThread == null) {
					var pausedProcess = mgr.Processes.FirstOrDefault(p => p.State == DbgProcessState.Paused);
					if (pausedProcess == null) {
						return new CallToolResult {
							Content = new List<ToolContent> { new ToolContent { Text = "No paused process found. Debugger may still be running. Use break_debugger first." } }
						};
					}
					currentThread = pausedProcess.Threads.FirstOrDefault();
				}

				if (currentThread == null) {
					return new CallToolResult {
						Content = new List<ToolContent> { new ToolContent { Text = "No thread is available." } }
					};
				}

				const int maxFrames = 50;
				var frames = new List<object>();
				var stackFrames = currentThread.GetFrames(maxFrames);
				try {
					foreach (var frame in stackFrames) {
						try {
							frames.Add(new {
								Index = frames.Count,
								FunctionToken = $"0x{frame.FunctionToken:X8}",
								FunctionOffset = frame.FunctionOffset,
								ModuleName = frame.Module?.Name ?? "Unknown",
								IsCurrentStatement = (frame.Flags & DbgStackFrameFlags.LocationIsNextStatement) != 0
							});
						}
						finally {
							frame.Close();
						}
					}
				}
				finally {
					// GetFrames() returns owned frames; they were closed in the loop above.
				}

				var result = JsonSerializer.Serialize(new {
					ThreadId = currentThread.Id,
					ManagedId = currentThread.ManagedId,
					FrameCount = frames.Count,
					MaxFramesReturned = maxFrames,
					Frames = frames
				}, new JsonSerializerOptions { WriteIndented = true });

				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = result } }
				};
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "GetCallStack failed");
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = $"Error getting call stack: {ex.Message}" } },
					IsError = true
				};
			}
		}

		// ── start_debugging ──────────────────────────────────────────────────────

		/// <summary>
		/// Launches an EXE under the dnSpy debugger.
		/// Arguments: exe_path* | arguments | working_directory | break_kind (default "EntryPoint")
		/// </summary>
		public CallToolResult StartDebugging(Dictionary<string, object>? arguments) {
			if (arguments == null)
				throw new ArgumentException("Arguments required");
			if (!arguments.TryGetValue("exe_path", out var exePathObj))
				throw new ArgumentException("exe_path is required");

			var exePath = exePathObj.ToString() ?? string.Empty;
			if (!System.IO.File.Exists(exePath))
				throw new ArgumentException($"File not found: {exePath}");

			string? commandLine = null;
			if (arguments.TryGetValue("arguments", out var argsObj))
				commandLine = argsObj?.ToString();

			string? workingDir = null;
			if (arguments.TryGetValue("working_directory", out var wdObj))
				workingDir = wdObj?.ToString();
			if (string.IsNullOrEmpty(workingDir))
				workingDir = System.IO.Path.GetDirectoryName(exePath);

			string breakKind = PredefinedBreakKinds.EntryPoint;
			if (arguments.TryGetValue("break_kind", out var bkObj) && bkObj?.ToString() is string bkStr && !string.IsNullOrEmpty(bkStr))
				breakKind = bkStr;

			var opts = new DotNetFrameworkStartDebuggingOptions {
				Filename         = exePath,
				CommandLine      = commandLine ?? string.Empty,
				WorkingDirectory = workingDir,
				BreakKind        = breakKind,
			};

			var error = dbgManager.Value.Start(opts);

			var result = JsonSerializer.Serialize(new {
				Started   = error == null,
				ExePath   = exePath,
				BreakKind = breakKind,
				Error     = error,
				Note      = error == null
					? "Process launched asynchronously. Use get_debugger_state to check when it is paused."
					: null
			}, new JsonSerializerOptions {
				WriteIndented = true,
				DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
			});

			return new CallToolResult {
				Content = new List<ToolContent> { new ToolContent { Text = result } }
			};
		}

		// ── attach_to_process ────────────────────────────────────────────────────

		/// <summary>
		/// Attaches the dnSpy debugger to a running .NET process by PID.
		/// Arguments: process_id*
		/// </summary>
		public CallToolResult AttachToProcess(Dictionary<string, object>? arguments) {
			if (arguments == null)
				throw new ArgumentException("Arguments required");
			if (!arguments.TryGetValue("process_id", out var pidObj))
				throw new ArgumentException("process_id is required");

			int pid = 0;
			if (pidObj is JsonElement pidElem) pidElem.TryGetInt32(out pid);
			else int.TryParse(pidObj?.ToString(), out pid);
			if (pid <= 0)
				throw new ArgumentException("process_id must be a positive integer");

			var processes = attachableProcessesService.Value
				.GetAttachableProcessesAsync(null, new[] { pid }, null)
				.GetAwaiter().GetResult();

			if (processes.Length == 0)
				throw new ArgumentException(
					$"No attachable .NET process found with PID {pid}. " +
					"The process may not exist or does not expose a supported runtime.");

			// Attach to the first matching entry (each entry represents one CLR runtime in the process)
			var target = processes[0];
			target.Attach();

			var result = JsonSerializer.Serialize(new {
				Attached    = true,
				ProcessId   = pid,
				RuntimeName = target.RuntimeName,
				Name        = target.Name,
				Note        = "Attach is asynchronous. Use get_debugger_state to verify the session."
			}, new JsonSerializerOptions { WriteIndented = true });

			return new CallToolResult {
				Content = new List<ToolContent> { new ToolContent { Text = result } }
			};
		}

		// ── Helpers ─────────────────────────────────────────────────────────────

	// ── Stepping ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Steps over the current statement.
	/// Arguments: thread_id (optional), process_id (optional), timeout_seconds (optional, default 30)
	/// </summary>
	public CallToolResult StepOver(Dictionary<string, object>? arguments) =>
		StepImpl(arguments, DbgStepKind.StepOver);

	/// <summary>
	/// Steps into the current statement (enters called methods).
	/// Arguments: thread_id (optional), process_id (optional), timeout_seconds (optional, default 30)
	/// </summary>
	public CallToolResult StepInto(Dictionary<string, object>? arguments) =>
		StepImpl(arguments, DbgStepKind.StepInto);

	/// <summary>
	/// Steps out of the current method (runs until caller resumes).
	/// Arguments: thread_id (optional), process_id (optional), timeout_seconds (optional, default 30)
	/// </summary>
	public CallToolResult StepOut(Dictionary<string, object>? arguments) =>
		StepImpl(arguments, DbgStepKind.StepOut);

	/// <summary>
	/// Returns the current execution location (top frame of current/first paused thread).
	/// Arguments: thread_id (optional), process_id (optional)
	/// </summary>
	public CallToolResult GetCurrentLocation(Dictionary<string, object>? arguments) {
		var mgr = dbgManager.Value;
		if (!mgr.IsDebugging)
			throw new InvalidOperationException("No active debug session.");

		var thread = ResolveThread(mgr, arguments);
		if (thread == null)
			throw new InvalidOperationException(
				"No paused thread found. Use break_debugger or wait for a breakpoint.");

		return System.Windows.Application.Current.Dispatcher.Invoke(() => {
			var frames = thread.GetFrames(1);
			if (frames.Length == 0)
				return new CallToolResult { Content = new List<ToolContent> {
					new ToolContent { Text = "Thread has no stack frames." } } };
			var f = frames[0];
			var json = JsonSerializer.Serialize(new {
				ThreadId        = (int)thread.Id,
				FunctionToken   = $"0x{f.FunctionToken:X8}",
				FunctionOffset  = f.FunctionOffset,
				ModuleName      = f.Module?.Name ?? "?",
				IsNextStatement = (f.Flags & DbgStackFrameFlags.LocationIsNextStatement) != 0
			}, new JsonSerializerOptions { WriteIndented = true });
			f.Close();
			return new CallToolResult {
				Content = new List<ToolContent> { new ToolContent { Text = json } }
			};
		});
	}

	/// <summary>
	/// Polls until any process becomes paused, then returns info about it.
	/// Arguments: timeout_seconds (optional, default 30)
	/// </summary>
	public CallToolResult WaitForPause(Dictionary<string, object>? arguments) {
		var mgr = dbgManager.Value;
		if (!mgr.IsDebugging)
			throw new InvalidOperationException("No active debug session.");

		int timeoutSeconds = 30;
		if (arguments != null && arguments.TryGetValue("timeout_seconds", out var tso)) {
			if (tso is JsonElement je && je.TryGetInt32(out var ti)) timeoutSeconds = Math.Max(1, ti);
			else if (int.TryParse(tso?.ToString(), out var ti2)) timeoutSeconds = Math.Max(1, ti2);
		}

		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (DateTime.UtcNow < deadline) {
			var paused = mgr.Processes.FirstOrDefault(p => p.State == DbgProcessState.Paused);
			if (paused != null) {
				var json = JsonSerializer.Serialize(new {
					Paused      = true,
					ProcessId   = (int)paused.Id,
					ThreadCount = paused.Threads.Length
				}, new JsonSerializerOptions { WriteIndented = true });
				return new CallToolResult {
					Content = new List<ToolContent> { new ToolContent { Text = json } }
				};
			}
			Thread.Sleep(100);
		}
		throw new TimeoutException($"Debugger did not pause within {timeoutSeconds}s.");
	}

	// ── Private stepping helpers ──────────────────────────────────────────────

	DbgThread? ResolveThread(DbgManager mgr, Dictionary<string, object>? args) {
		// 1. Explicit thread_id
		if (args != null && args.TryGetValue("thread_id", out var tidObj)) {
			if (uint.TryParse(tidObj?.ToString(), out var tidVal))
				foreach (var p in mgr.Processes)
					foreach (var t in p.Threads)
						if (t.Id == tidVal) return t;
			throw new ArgumentException($"Thread {tidObj} not found");
		}
		// 2. Optional process_id filter
		DbgProcess? targetProc = null;
		if (args != null && args.TryGetValue("process_id", out var pidObj)) {
			if (uint.TryParse(pidObj?.ToString(), out var pidVal))
				targetProc = mgr.Processes.FirstOrDefault(p => p.Id == pidVal);
			if (targetProc == null) throw new ArgumentException($"Process {pidObj} not found");
		}
		// 3. Current thread (honors process_id if set)
		var cur = mgr.CurrentThread?.Current;
		if (cur != null && (targetProc == null || cur.Process == targetProc)) return cur;
		// 4. First paused thread in target/any process
		var procs = targetProc != null ? new[] { targetProc } : mgr.Processes.ToArray();
		return procs.Where(p => p.State == DbgProcessState.Paused)
		            .SelectMany(p => p.Threads).FirstOrDefault();
	}

	CallToolResult StepImpl(Dictionary<string, object>? arguments, DbgStepKind stepKind) {
		var mgr = dbgManager.Value;
		if (!mgr.IsDebugging)
			throw new InvalidOperationException(
				"No active debug session. Use start_debugging or attach_to_process first.");

		// Validate that the process is actually paused before attempting to step.
		// IsRunning is bool? — null = no session, true = running, false = paused.
		if (mgr.IsRunning == true)
			throw new InvalidOperationException(
				"Cannot step: the process is currently running. " +
				"Use break_debugger or wait_for_pause to pause it first.");

		var thread = ResolveThread(mgr, arguments);
		if (thread == null)
			throw new InvalidOperationException(
				"No paused thread found. Use break_debugger or wait for a breakpoint to hit.");

		// Default 15s — leaves a safety margin before typical MCP client timeouts (~30s).
		int timeoutSeconds = 15;
		if (arguments != null && arguments.TryGetValue("timeout_seconds", out var tso)) {
			if (tso is JsonElement je && je.TryGetInt32(out var ti)) timeoutSeconds = Math.Max(1, ti);
			else if (int.TryParse(tso?.ToString(), out var ti2)) timeoutSeconds = Math.Max(1, ti2);
		}

		var mre = new ManualResetEventSlim(false);
		string? stepError = null;
		object? frameInfo = null;

		System.Windows.Application.Current.Dispatcher.Invoke(() => {
			var stepper = thread.CreateStepper();
			if (!stepper.CanStep) {
				stepper.Close();
				throw new InvalidOperationException(
					"Cannot step: stepper reports CanStep=false. " +
					"The thread may not be at a steppable IL instruction (e.g. native frame or JIT thunk). " +
					"Try step_into or verify the current frame with get_current_location.");
			}
			stepper.StepComplete += (s, e) => {
				stepError = e.Error;
				var frames = e.Thread.GetFrames(1);
				if (frames.Length > 0) {
					frameInfo = new {
						ThreadId        = (int)e.Thread.Id,
						FunctionToken   = $"0x{frames[0].FunctionToken:X8}",
						FunctionOffset  = frames[0].FunctionOffset,
						ModuleName      = frames[0].Module?.Name ?? "?",
						IsNextStatement = (frames[0].Flags & DbgStackFrameFlags.LocationIsNextStatement) != 0
					};
					frames[0].Close();
				}
				mre.Set();
			};
			stepper.Step(stepKind, autoClose: true);
		});

		// Poll every 100 ms instead of a single blocking wait.
		// This allows us to detect process death (crash, unhandled exception, detach) immediately,
		// rather than waiting the full timeout when StepComplete will never fire.
		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (!mre.Wait(100)) {
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException(
					$"Step did not complete within {timeoutSeconds}s. " +
					"The process may have resumed without triggering StepComplete, " +
					"or a dialog may be blocking the debugger. " +
					"Check get_debugger_state and list_dialogs.");
			if (!mgr.IsDebugging)
				throw new InvalidOperationException(
					"The debug session ended while waiting for the step to complete. " +
					"The process likely crashed due to an unhandled exception (e.g. Anti-Tamper protection " +
					"detected a patched binary and caused an AccessViolationException in the module .cctor). " +
					"To bypass Anti-Tamper: use unpack_from_memory (break_kind=EntryPoint) BEFORE patching, " +
					"or neutralize the Anti-Tamper .cctor with patch_method_to_ret before saving.");
		}

		if (stepError != null)
			return new CallToolResult {
				Content = new List<ToolContent> { new ToolContent {
					Text = $"Step completed with error: {stepError}" } },
				IsError = true
			};

		var json = JsonSerializer.Serialize(new {
			StepKind = stepKind.ToString(),
			Location = frameInfo
		}, new JsonSerializerOptions { WriteIndented = true });
		return new CallToolResult {
			Content = new List<ToolContent> { new ToolContent { Text = json } }
		};
	}

	// ── Exception breakpoints ─────────────────────────────────────────────────

	/// <summary>
	/// Adds or updates an exception breakpoint so the debugger pauses when the exception is thrown.
	/// Arguments: exception_type* | first_chance (bool, default true) | second_chance (bool, default false) | category (optional, default "DotNet")
	/// </summary>
	public CallToolResult SetExceptionBreakpoint(Dictionary<string, object>? arguments) {
		if (arguments == null || !arguments.TryGetValue("exception_type", out var typeObj))
			throw new ArgumentException("exception_type is required");
		string exceptionType = typeObj?.ToString() ?? "";
		if (string.IsNullOrEmpty(exceptionType))
			throw new ArgumentException("exception_type cannot be empty");

		string category = PredefinedExceptionCategories.DotNet;
		if (arguments.TryGetValue("category", out var catObj) && !string.IsNullOrEmpty(catObj?.ToString()))
			category = catObj!.ToString()!;

		bool firstChance = true;
		if (arguments.TryGetValue("first_chance", out var fcObj)) {
			if (fcObj is JsonElement fce) firstChance = fce.ValueKind != JsonValueKind.False;
			else bool.TryParse(fcObj?.ToString(), out firstChance);
		}
		bool secondChance = false;
		if (arguments.TryGetValue("second_chance", out var scObj)) {
			if (scObj is JsonElement sce) secondChance = sce.ValueKind != JsonValueKind.False;
			else bool.TryParse(scObj?.ToString(), out secondChance);
		}

		var flags = DbgExceptionDefinitionFlags.None;
		if (firstChance)  flags |= DbgExceptionDefinitionFlags.StopFirstChance;
		if (secondChance) flags |= DbgExceptionDefinitionFlags.StopSecondChance;

		var id  = new DbgExceptionId(category, exceptionType);
		var svc = exceptionSettingsService.Value;
		var settings = new DbgExceptionSettings(flags);

		string action;
		if (svc.TryGetDefinition(id, out _)) {
			svc.Modify(id, settings);
			action = "modified";
		} else {
			var def = new DbgExceptionDefinition(id, flags);
			svc.Add(new DbgExceptionSettingsInfo(def, settings));
			action = "added";
		}

		var json = JsonSerializer.Serialize(new {
			Action        = action,
			ExceptionType = exceptionType,
			Category      = category,
			FirstChance   = firstChance,
			SecondChance  = secondChance,
			Note          = "Use continue_debugger to resume; the debugger will now break when this exception is thrown."
		}, new JsonSerializerOptions { WriteIndented = true });
		return new CallToolResult { Content = new List<ToolContent> { new ToolContent { Text = json } } };
	}

	/// <summary>
	/// Removes an exception breakpoint previously set via set_exception_breakpoint.
	/// Arguments: exception_type* | category (optional, default "DotNet")
	/// </summary>
	public CallToolResult RemoveExceptionBreakpoint(Dictionary<string, object>? arguments) {
		if (arguments == null || !arguments.TryGetValue("exception_type", out var typeObj))
			throw new ArgumentException("exception_type is required");
		string exceptionType = typeObj?.ToString() ?? "";
		if (string.IsNullOrEmpty(exceptionType))
			throw new ArgumentException("exception_type cannot be empty");

		string category = PredefinedExceptionCategories.DotNet;
		if (arguments.TryGetValue("category", out var catObj) && !string.IsNullOrEmpty(catObj?.ToString()))
			category = catObj!.ToString()!;

		var id  = new DbgExceptionId(category, exceptionType);
		var svc = exceptionSettingsService.Value;

		if (!svc.TryGetDefinition(id, out _))
			return new CallToolResult { Content = new List<ToolContent> {
				new ToolContent { Text = $"Exception '{exceptionType}' not found in the exception settings list. Use list_exception_breakpoints to see active entries." } } };

		svc.Remove(new[] { id });
		return new CallToolResult { Content = new List<ToolContent> {
			new ToolContent { Text = $"Exception breakpoint removed: {category} \u2014 {exceptionType}" } } };
	}

	/// <summary>
	/// Lists all exception breakpoints that have StopFirstChance or StopSecondChance enabled.
	/// Arguments: none
	/// </summary>
	public CallToolResult ListExceptionBreakpoints() {
		var svc = exceptionSettingsService.Value;
		var active = svc.Exceptions
			.Where(e => e.Settings.Flags != DbgExceptionDefinitionFlags.None)
			.Select(e => new {
				Category      = e.Definition.Id.Category,
				ExceptionType = e.Definition.Id.HasName
					? e.Definition.Id.Name
					: (e.Definition.Id.IsDefaultId ? "<<default>>" : $"Code:0x{e.Definition.Id.Code:X8}"),
				FirstChance   = (e.Settings.Flags & DbgExceptionDefinitionFlags.StopFirstChance)  != 0,
				SecondChance  = (e.Settings.Flags & DbgExceptionDefinitionFlags.StopSecondChance) != 0
			})
			.OrderBy(e => e.Category).ThenBy(e => e.ExceptionType)
			.ToList();

		var json = JsonSerializer.Serialize(new {
			Count = active.Count,
			ExceptionBreakpoints = active
		}, new JsonSerializerOptions { WriteIndented = true });
		return new CallToolResult { Content = new List<ToolContent> { new ToolContent { Text = json } } };
	}

		int? GetOptionalInt(Dictionary<string, object>? arguments, string key) {
			if (arguments == null || !arguments.TryGetValue(key, out var value) || value == null)
				return null;
			if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt))
				return jsonInt;
			if (int.TryParse(value.ToString(), out var parsedInt))
				return parsedInt;
			throw new ArgumentException($"{key} must be an integer.");
		}

		static bool GetOptionalBool(Dictionary<string, object>? arguments, string key, bool defaultValue) {
			if (arguments == null || !arguments.TryGetValue(key, out var value) || value == null)
				return defaultValue;
			if (value is JsonElement element) {
				if (element.ValueKind == JsonValueKind.True) return true;
				if (element.ValueKind == JsonValueKind.False) return false;
				if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var jsonBool))
					return jsonBool;
			}
			if (bool.TryParse(value.ToString(), out var parsedBool))
				return parsedBool;
			return defaultValue;
		}

		static string? GetOptionalString(Dictionary<string, object>? arguments, string key) {
			if (arguments == null || !arguments.TryGetValue(key, out var value) || value == null)
				return null;
			var text = value is JsonElement element && element.ValueKind == JsonValueKind.String ? element.GetString() : value.ToString();
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}

		object ResolveReference(Dictionary<string, object>? arguments) {
			if (arguments == null || arguments.Count == 0)
				throw new ArgumentException("Reference arguments are required.");

			var assemblyName = GetOptionalString(arguments, "assembly_name");
			var filePath = GetOptionalString(arguments, "file_path");
			var assembly = !string.IsNullOrWhiteSpace(filePath)
				? FindAssemblyByName(assemblyName ?? string.Empty, filePath)
				: !string.IsNullOrWhiteSpace(assemblyName) ? FindAssemblyByName(assemblyName!) : null;

			if (arguments.TryGetValue("metadata_token", out var tokenObj)) {
				if (assembly == null)
					throw new ArgumentException("assembly_name or file_path is required when resolving metadata_token.");
				return ResolveTokenReference(assembly, ParseMetadataToken(tokenObj));
			}

			if (assembly == null)
				throw new ArgumentException("assembly_name or file_path is required.");

			var moduleName = GetOptionalString(arguments, "module_name");
			if (!string.IsNullOrEmpty(moduleName)) {
				var module = assembly.Modules.FirstOrDefault(a =>
					string.Equals(a.Name, moduleName, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(a.Location, moduleName, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(System.IO.Path.GetFileName(a.Location), moduleName, StringComparison.OrdinalIgnoreCase));
				if (module == null)
					throw new ArgumentException($"Module not found: {moduleName}");
				if (GetOptionalString(arguments, "type_full_name") == null)
					return module;
			}

			var typeFullName = GetOptionalString(arguments, "type_full_name");
			if (typeFullName == null)
				return assembly;

			var type = FindTypeInAssembly(assembly, typeFullName);
			if (type == null)
				throw new ArgumentException($"Type not found: {typeFullName}");

			var methodName = GetOptionalString(arguments, "method_name");
			if (methodName != null) {
				var methodSignature = GetOptionalString(arguments, "method_signature");
				var method = type.Methods.FirstOrDefault(a =>
					a.Name.String.Equals(methodName, StringComparison.Ordinal) &&
					(methodSignature == null || string.Equals(a.MethodSig?.ToString(), methodSignature, StringComparison.Ordinal)));
				if (method == null)
					throw new ArgumentException($"Method not found: {methodName}");
				return method;
			}

			var fieldName = GetOptionalString(arguments, "field_name");
			if (fieldName != null) {
				var field = type.Fields.FirstOrDefault(a => a.Name.String.Equals(fieldName, StringComparison.Ordinal));
				if (field == null)
					throw new ArgumentException($"Field not found: {fieldName}");
				return field;
			}

			var propertyName = GetOptionalString(arguments, "property_name");
			if (propertyName != null) {
				var property = type.Properties.FirstOrDefault(a => a.Name.String.Equals(propertyName, StringComparison.Ordinal));
				if (property == null)
					throw new ArgumentException($"Property not found: {propertyName}");
				return property;
			}

			var eventName = GetOptionalString(arguments, "event_name");
			if (eventName != null) {
				var @event = type.Events.FirstOrDefault(a => a.Name.String.Equals(eventName, StringComparison.Ordinal));
				if (@event == null)
					throw new ArgumentException($"Event not found: {eventName}");
				return @event;
			}

			return type;
		}

		object ResolveTokenReference(AssemblyDef assembly, uint rawToken) {
			var token = new MDToken(rawToken);
			foreach (var module in assembly.Modules) {
				try {
					var resolved = module.ResolveToken(token);
					if (resolved != null)
						return resolved;
				}
				catch {
				}
			}

			throw new ArgumentException($"Token 0x{rawToken:X8} could not be resolved in assembly {assembly.Name.String}.");
		}

		static uint ParseMetadataToken(object tokenObj) {
			switch (tokenObj) {
				case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out var numericToken):
					return numericToken;
				case JsonElement element when element.ValueKind == JsonValueKind.String:
					return ParseMetadataTokenString(element.GetString());
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

		static uint ParseMetadataTokenString(string? tokenText) {
			if (string.IsNullOrWhiteSpace(tokenText))
				throw new ArgumentException("metadata_token must be a non-empty metadata token string or integer.");

			var trimmed = tokenText.Trim();
			if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
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

		static bool ReadBool(object? value, bool defaultValue) {
			if (value == null)
				return defaultValue;
			if (value is bool boolValue)
				return boolValue;
			if (value is JsonElement element) {
				if (element.ValueKind == JsonValueKind.True)
					return true;
				if (element.ValueKind == JsonValueKind.False)
					return false;
				if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsedBool))
					return parsedBool;
			}
			return bool.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
		}

		DocumentTreeNodeData? FindNodeForReference(object reference) {
			return reference switch {
				AssemblyDef assembly => documentTreeView.FindNode(assembly),
				ModuleDef module => documentTreeView.FindNode(module),
				TypeDef type => documentTreeView.FindNode(type),
				MethodDef method => documentTreeView.FindNode(method),
				FieldDef field => documentTreeView.FindNode(field),
				PropertyDef property => documentTreeView.FindNode(property),
				EventDef @event => documentTreeView.FindNode(@event),
				_ => documentTreeView.FindNode(reference)
			};
		}

		Dictionary<string, object?> DescribeBreakpoint(DbgCodeBreakpoint breakpoint) {
			return new Dictionary<string, object?> {
				["id"] = breakpoint.Id,
				["is_enabled"] = breakpoint.IsEnabled,
				["is_hidden"] = breakpoint.IsHidden,
				["is_temporary"] = breakpoint.IsTemporary,
				["is_one_shot"] = breakpoint.IsOneShot,
				["location_type"] = breakpoint.Location.Type,
				["location"] = breakpoint.Location.ToString(),
				["condition"] = breakpoint.Condition is DbgCodeBreakpointCondition condition
					? new Dictionary<string, object?> {
						["kind"] = condition.Kind.ToString(),
						["expression"] = condition.Condition
					}
					: null,
				["hit_count"] = breakpoint.HitCount is DbgCodeBreakpointHitCount hitCount
					? new Dictionary<string, object?> {
						["kind"] = hitCount.Kind.ToString(),
						["count"] = hitCount.Count
					}
					: null,
				["filter"] = breakpoint.Filter?.ToString(),
				["trace"] = breakpoint.Trace?.ToString(),
				["labels"] = breakpoint.Labels.ToArray(),
				["bound_breakpoints_message"] = new Dictionary<string, object?> {
					["severity"] = breakpoint.BoundBreakpointsMessage.Severity.ToString(),
					["message"] = breakpoint.BoundBreakpointsMessage.Message
				},
				["bound_breakpoints"] = breakpoint.BoundBreakpoints.Select(a => new Dictionary<string, object?> {
					["process_id"] = (int)a.Process.Id,
					["runtime_name"] = a.Runtime.RuntimeKindGuid.ToString(),
					["module_name"] = a.Module?.Name,
					["address"] = a.HasAddress ? $"0x{a.Address:X}" : null,
					["message"] = new Dictionary<string, object?> {
						["severity"] = a.Message.Severity.ToString(),
						["message"] = a.Message.Message
					}
				}).ToArray()
			};
		}

		Dictionary<string, object?> DescribeNode(DocumentTreeNodeData node) {
			var payload = new Dictionary<string, object?> {
				["node_type"] = node.GetType().Name,
				["display_text"] = node.ToString(),
				["path"] = GetNodePath(node)
			};

			if (node is IMDTokenNode tokenNode && tokenNode.Reference is IMDTokenProvider provider)
				payload["metadata_token"] = $"0x{provider.MDToken.Raw:X8}";

			switch (node) {
				case AssemblyDocumentNode assemblyNode:
					var assemblyDocument = assemblyNode.Document;
					payload["assembly_name"] = assemblyDocument?.AssemblyDef?.Name.String;
					payload["full_name"] = assemblyDocument?.AssemblyDef?.FullName;
					payload["file_path"] = assemblyDocument?.Filename;
					break;

				case ModuleDocumentNode moduleNode:
					var moduleDocument = moduleNode.Document;
					payload["module_name"] = moduleDocument?.ModuleDef?.Name;
					payload["file_path"] = moduleDocument?.Filename;
					payload["assembly_name"] = moduleDocument?.AssemblyDef?.Name.String;
					break;

				case NamespaceNode namespaceNode:
					payload["namespace"] = namespaceNode.Name;
					payload["module_name"] = namespaceNode.GetAncestorOrSelf<ModuleDocumentNode>()?.Document?.ModuleDef?.Name;
					break;

				case TypeNode typeNode:
					payload["type_full_name"] = typeNode.TypeDef.FullName;
					payload["namespace"] = typeNode.TypeDef.Namespace.String;
					payload["assembly_name"] = typeNode.TypeDef.Module.Assembly?.Name.String;
					break;

				case MethodNode methodNode:
					payload["method_name"] = methodNode.MethodDef.Name.String;
					payload["method_full_name"] = methodNode.MethodDef.FullName;
					payload["declaring_type"] = methodNode.MethodDef.DeclaringType?.FullName;
					payload["signature"] = methodNode.MethodDef.MethodSig?.ToString();
					break;

				case FieldNode fieldNode:
					payload["field_name"] = fieldNode.FieldDef.Name.String;
					payload["field_full_name"] = fieldNode.FieldDef.FullName;
					payload["declaring_type"] = fieldNode.FieldDef.DeclaringType?.FullName;
					break;

				case PropertyNode propertyNode:
					payload["property_name"] = propertyNode.PropertyDef.Name.String;
					payload["property_full_name"] = propertyNode.PropertyDef.FullName;
					payload["declaring_type"] = propertyNode.PropertyDef.DeclaringType?.FullName;
					break;

				case EventNode eventNode:
					payload["event_name"] = eventNode.EventDef.Name.String;
					payload["event_full_name"] = eventNode.EventDef.FullName;
					payload["declaring_type"] = eventNode.EventDef.DeclaringType?.FullName;
					break;
			}

			return payload;
		}

		List<string> GetNodePath(DocumentTreeNodeData node) {
			var segments = new List<string>();
			TreeNodeData? current = node;
			while (current != null) {
				var text = current.ToString();
				if (!string.IsNullOrWhiteSpace(text))
					segments.Add(text);
				current = current.TreeNode.Parent?.Data;
			}
			segments.Reverse();
			return segments;
		}

		Dictionary<string, object?> DescribeTab(IDocumentTab tab) {
			var viewer = tab.TryGetDocumentViewer();
			var selectedReference = viewer?.SelectedReference;

			return new Dictionary<string, object?> {
				["title"] = tab.Content.Title,
				["content_type"] = tab.Content.GetType().Name,
				["is_active_tab"] = tab.IsActiveTab,
				["can_navigate_backward"] = tab.CanNavigateBackward,
				["can_navigate_forward"] = tab.CanNavigateForward,
				["is_async_exec_in_progress"] = tab.IsAsyncExecInProgress,
				["node_count"] = tab.Content.Nodes.Count(),
				["nodes"] = tab.Content.Nodes.Take(8).Select(DescribeNode).ToArray(),
				["has_document_viewer"] = viewer != null,
				["selected_reference"] = selectedReference != null
					? new Dictionary<string, object?> {
						["span_start"] = selectedReference.Value.Span.Start,
						["span_length"] = selectedReference.Value.Span.Length,
						["reference"] = DescribeReference(selectedReference.Value.Data.Reference)
					}
					: null
			};
		}

		Dictionary<string, object?> DescribeReference(object? reference) {
			var payload = new Dictionary<string, object?> {
				["reference_kind"] = reference?.GetType().Name ?? "null",
				["display_text"] = reference?.ToString()
			};

			switch (reference) {
				case AssemblyDef assembly:
					payload["assembly_name"] = assembly.Name.String;
					payload["full_name"] = assembly.FullName;
					break;

				case ModuleDef module:
					payload["module_name"] = module.Name;
					payload["file_path"] = module.Location;
					payload["assembly_name"] = module.Assembly?.Name.String;
					break;

				case TypeDef type:
					payload["type_full_name"] = type.FullName;
					payload["namespace"] = type.Namespace.String;
					payload["metadata_token"] = $"0x{type.MDToken.Raw:X8}";
					break;

				case MethodDef method:
					payload["method_name"] = method.Name.String;
					payload["full_name"] = method.FullName;
					payload["declaring_type"] = method.DeclaringType?.FullName;
					payload["signature"] = method.MethodSig?.ToString();
					payload["metadata_token"] = $"0x{method.MDToken.Raw:X8}";
					break;

				case FieldDef field:
					payload["field_name"] = field.Name.String;
					payload["full_name"] = field.FullName;
					payload["declaring_type"] = field.DeclaringType?.FullName;
					payload["metadata_token"] = $"0x{field.MDToken.Raw:X8}";
					break;

				case PropertyDef property:
					payload["property_name"] = property.Name.String;
					payload["full_name"] = property.FullName;
					payload["declaring_type"] = property.DeclaringType?.FullName;
					payload["metadata_token"] = $"0x{property.MDToken.Raw:X8}";
					break;

				case EventDef @event:
					payload["event_name"] = @event.Name.String;
					payload["full_name"] = @event.FullName;
					payload["declaring_type"] = @event.DeclaringType?.FullName;
					payload["metadata_token"] = $"0x{@event.MDToken.Raw:X8}";
					break;

				case IMDTokenProvider provider:
					payload["metadata_token"] = $"0x{provider.MDToken.Raw:X8}";
					break;
			}

			return payload;
		}

		sealed class KnownToolWindowDescriptor {
			public string Context { get; init; } = string.Empty;
			public string Title { get; init; } = string.Empty;
			public Guid Guid { get; init; }
			public bool RequiresDebugging { get; init; }
		}

		static IReadOnlyList<KnownToolWindowDescriptor> GetKnownToolWindows() => new[] {
			new KnownToolWindowDescriptor { Context = "code_breakpoints", Title = "Code Breakpoints", Guid = new Guid("E5745D58-4DCB-4D92-B786-4E1635C86EED"), RequiresDebugging = false },
			new KnownToolWindowDescriptor { Context = "module_breakpoints", Title = "Module Breakpoints", Guid = new Guid("9D7D28F0-F031-4439-99BF-F7B747FA4B19"), RequiresDebugging = false },
			new KnownToolWindowDescriptor { Context = "call_stack", Title = "Call Stack", Guid = new Guid("0E53B79D-EC30-44B6-86A3-DFFCE364EB4A"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "autos", Title = "Autos", Guid = new Guid("6E0232EC-3D88-4087-A571-CC1184D86645"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "locals", Title = "Locals", Guid = new Guid("D799829F-CAE3-4F8F-AD81-1732ABC50636"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "static_fields", Title = "Static Fields", Guid = new Guid("F46A6B46-4DA1-46BC-A279-2E2292069BE9"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "threads", Title = "Threads", Guid = new Guid("3C01719C-B6B5-4261-9CD4-3EDCE1032E5C"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "modules", Title = "Modules", Guid = new Guid("8C95EB2E-25F4-4D2F-A00D-A303754990DF"), RequiresDebugging = true },
			new KnownToolWindowDescriptor { Context = "exceptions", Title = "Exceptions", Guid = new Guid("82575354-AB18-408B-846B-AA585B7B2B4A"), RequiresDebugging = false },
			new KnownToolWindowDescriptor { Context = "processes", Title = "Processes", Guid = new Guid("F1EFB8BE-8941-4BE4-ACC4-ACA8809394BB"), RequiresDebugging = true },
		};

		static KnownToolWindowDescriptor ResolveKnownToolWindow(string context) {
			var normalized = context.Trim().ToLowerInvariant().Replace('-', '_');
			return GetKnownToolWindows().FirstOrDefault(a => a.Context == normalized)
				?? throw new ArgumentException($"Unknown tool window context: {context}");
		}

	AssemblyDef? FindAssemblyByName(string name, string? filePath = null) {
			return UiThreadHelper.Invoke(() => {
				if (!string.IsNullOrEmpty(filePath)) {
					var normalized = filePath!.Replace('/', '\\');
					var byPath = documentTreeView.GetAllModuleNodes()
						.FirstOrDefault(m => (m.Document?.Filename ?? "").Replace('/', '\\')
							.Equals(normalized, StringComparison.OrdinalIgnoreCase));
					if (byPath?.Document?.AssemblyDef != null)
						return byPath.Document.AssemblyDef;
				}

				return documentTreeView.GetAllModuleNodes()
					.Select(m => m.Document?.AssemblyDef)
					.FirstOrDefault(a => a != null && a.Name.String.Equals(name, StringComparison.OrdinalIgnoreCase));
			});
		}

		TypeDef? FindTypeInAssembly(AssemblyDef assembly, string fullName) =>
			assembly.Modules
				.SelectMany(m => GetAllTypesRecursive(m.Types))
				.FirstOrDefault(t => t.FullName.Equals(fullName, StringComparison.Ordinal));

		static System.Collections.Generic.IEnumerable<TypeDef> GetAllTypesRecursive(System.Collections.Generic.IEnumerable<TypeDef> types) {
			foreach (var t in types) {
				yield return t;
				foreach (var n in GetAllTypesRecursive(t.NestedTypes))
					yield return n;
			}
		}
	}
}
