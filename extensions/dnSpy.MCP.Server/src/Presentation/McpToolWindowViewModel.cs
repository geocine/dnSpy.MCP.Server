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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using dnSpy.Contracts.MVVM;
using dnSpy.MCP.Server.Communication;
using dnSpy.MCP.Server.Configuration;
using dnSpy.MCP.Server.Helper;

namespace dnSpy.MCP.Server.Presentation {
	[Export]
	[PartCreationPolicy(CreationPolicy.Shared)]
	sealed class McpToolWindowViewModel : ViewModelBase, IMcpSettingsActionHandler {
		readonly McpServer mcpServer;
		readonly McpSettings mcpSettings;
		readonly DispatcherTimer statusTimer;
		readonly List<string> logEntries = new List<string>();
		const int MaxLogEntries = 1000;

		string statusMessage = "Server is stopped";
		string host = "localhost";
		int port = 3100;
		bool enableServer = true;
		bool requireApiKey;
		string apiKey = string.Empty;
		bool enableRunScript;
		bool exposeBepInExDocs;
		bool exposeFullToolCatalog;
		bool allowImplicitDefaultSession = true;
		string implicitDefaultSessionId = "__implicit_http_session__";
		string logLevel = "Info";
		bool enableFileLogging = true;
		bool enableOutputPaneLogging = true;
		bool enableToolCallLogging = true;
		string logText = string.Empty;

		[ImportingConstructor]
		public McpToolWindowViewModel(McpServer mcpServer, McpSettings mcpSettings) {
			this.mcpServer = mcpServer;
			this.mcpSettings = mcpSettings;

			ApplyConfigToEditor(McpConfig.Instance);
			ResetLogs(McpLogger.GetRecentMessages());
			UpdateStatus();

			McpLogger.MessageLogged += OnMessageLogged;

			statusTimer = new DispatcherTimer(DispatcherPriority.Background) {
				Interval = TimeSpan.FromSeconds(1)
			};
			statusTimer.Tick += (s, e) => UpdateStatus();
			statusTimer.Start();
		}

		public IEnumerable<string> AvailableLogLevels => new[] { "Debug", "Info", "Warning", "Error" };
		public string ConfigFilePath => McpConfig.ConfigFilePath;
		public string LogFilePath => McpLogger.LogFilePath;
		public bool IsServerRunning => mcpServer.IsRunning;
		public string PrimaryServerActionLabel => IsServerRunning ? "Stop" : "Start";

		public string StatusMessage {
			get => statusMessage;
			private set {
				if (statusMessage != value) {
					statusMessage = value;
					OnPropertyChanged(nameof(StatusMessage));
				}
			}
		}

		public bool EnableServer {
			get => enableServer;
			set {
				if (enableServer != value) {
					enableServer = value;
					OnPropertyChanged(nameof(EnableServer));
				}
			}
		}

		public string Host {
			get => host;
			set {
				if (host != value) {
					host = value;
					OnPropertyChanged(nameof(Host));
				}
			}
		}

		public int Port {
			get => port;
			set {
				if (port != value) {
					port = value;
					OnPropertyChanged(nameof(Port));
				}
			}
		}

		public bool RequireApiKey {
			get => requireApiKey;
			set {
				if (requireApiKey != value) {
					requireApiKey = value;
					OnPropertyChanged(nameof(RequireApiKey));
				}
			}
		}

		public string ApiKey {
			get => apiKey;
			set {
				if (apiKey != value) {
					apiKey = value;
					OnPropertyChanged(nameof(ApiKey));
				}
			}
		}

		public bool EnableRunScript {
			get => enableRunScript;
			set {
				if (enableRunScript != value) {
					enableRunScript = value;
					OnPropertyChanged(nameof(EnableRunScript));
				}
			}
		}

		public bool ExposeBepInExDocs {
			get => exposeBepInExDocs;
			set {
				if (exposeBepInExDocs != value) {
					exposeBepInExDocs = value;
					OnPropertyChanged(nameof(ExposeBepInExDocs));
				}
			}
		}

		public bool ExposeFullToolCatalog {
			get => exposeFullToolCatalog;
			set {
				if (exposeFullToolCatalog != value) {
					exposeFullToolCatalog = value;
					OnPropertyChanged(nameof(ExposeFullToolCatalog));
				}
			}
		}

		public bool AllowImplicitDefaultSession {
			get => allowImplicitDefaultSession;
			set {
				if (allowImplicitDefaultSession != value) {
					allowImplicitDefaultSession = value;
					OnPropertyChanged(nameof(AllowImplicitDefaultSession));
				}
			}
		}

		public string ImplicitDefaultSessionId {
			get => implicitDefaultSessionId;
			set {
				if (implicitDefaultSessionId != value) {
					implicitDefaultSessionId = value;
					OnPropertyChanged(nameof(ImplicitDefaultSessionId));
				}
			}
		}

		public string LogLevel {
			get => logLevel;
			set {
				if (logLevel != value) {
					logLevel = value;
					OnPropertyChanged(nameof(LogLevel));
				}
			}
		}

		public bool EnableFileLogging {
			get => enableFileLogging;
			set {
				if (enableFileLogging != value) {
					enableFileLogging = value;
					OnPropertyChanged(nameof(EnableFileLogging));
				}
			}
		}

		public bool EnableOutputPaneLogging {
			get => enableOutputPaneLogging;
			set {
				if (enableOutputPaneLogging != value) {
					enableOutputPaneLogging = value;
					OnPropertyChanged(nameof(EnableOutputPaneLogging));
				}
			}
		}

		public bool EnableToolCallLogging {
			get => enableToolCallLogging;
			set {
				if (enableToolCallLogging != value) {
					enableToolCallLogging = value;
					OnPropertyChanged(nameof(EnableToolCallLogging));
				}
			}
		}

		public string LogText {
			get => logText;
			private set {
				if (logText != value) {
					logText = value;
					OnPropertyChanged(nameof(LogText));
				}
			}
		}

		public void SaveConfiguration(bool forceRestart = false) {
			var cfg = McpConfig.Instance;
			cfg.EnableServer = EnableServer;
			cfg.Host = Host;
			cfg.Port = Port;
			cfg.RequireApiKey = RequireApiKey;
			cfg.ApiKey = ApiKey;
			cfg.EnableRunScript = EnableRunScript;
			cfg.ExposeBepInExDocs = ExposeBepInExDocs;
			cfg.ExposeFullToolCatalog = ExposeFullToolCatalog;
			cfg.AllowImplicitDefaultSession = AllowImplicitDefaultSession;
			cfg.ImplicitDefaultSessionId = ImplicitDefaultSessionId;
			cfg.LogLevel = LogLevel;
			cfg.EnableFileLogging = EnableFileLogging;
			cfg.EnableOutputPaneLogging = EnableOutputPaneLogging;
			cfg.EnableToolCallLogging = EnableToolCallLogging;
			cfg.Save();

			ApplyEditorStateToRuntime(forceRestart);
			McpLogger.Info("MCP configuration saved from the MCP UI");
			UpdateStatus();
		}

		public void SaveConfiguration() => SaveConfiguration(forceRestart: false);

		public void ReloadConfiguration() {
			var cfg = McpConfig.Reload();
			ApplyConfigToEditor(cfg);
			ApplyEditorStateToRuntime(forceRestart: false);
			McpLogger.Info("MCP configuration reloaded from disk");
			UpdateStatus();
		}

		public void StartServer() {
			EnableServer = true;
			SaveConfiguration(forceRestart: false);
		}

		public void ToggleServer() {
			if (IsServerRunning)
				StopServer();
			else
				StartServer();
		}

		public void StopServer() {
			EnableServer = false;
			SaveConfiguration(forceRestart: false);
		}

		public void RestartServer() {
			if (!EnableServer)
				EnableServer = true;
			SaveConfiguration(forceRestart: true);
		}

		public void ClearLogs() {
			McpLogger.ClearInMemoryMessages();
			logEntries.Clear();
			LogText = string.Empty;
		}

		public void OpenConfigFile() => OpenInExplorer(ConfigFilePath, selectFile: true);
		public void OpenLogFile() => OpenInExplorer(LogFilePath, selectFile: true);
		public void OpenLogDirectory() => OpenInExplorer(Path.GetDirectoryName(LogFilePath) ?? LogFilePath, selectFile: false);

		void ApplyEditorStateToRuntime(bool forceRestart) {
			var hostChanged = !string.Equals(mcpSettings.Host, Host, StringComparison.OrdinalIgnoreCase);
			var portChanged = mcpSettings.Port != Port;
			var enableChanged = mcpSettings.EnableServer != EnableServer;

			mcpSettings.Host = Host;
			mcpSettings.Port = Port;

			if (enableChanged) {
				mcpSettings.EnableServer = EnableServer;
			}
			else if (EnableServer) {
				if (forceRestart || hostChanged || portChanged) {
					if (mcpServer.IsRunning)
						mcpServer.Restart();
					else
						mcpServer.Start();
				}
			}
			else if (mcpServer.IsRunning) {
				mcpServer.Stop();
			}
		}

		void ApplyConfigToEditor(McpConfig cfg) {
			EnableServer = cfg.EnableServer;
			Host = cfg.Host;
			Port = cfg.Port;
			RequireApiKey = cfg.RequireApiKey;
			ApiKey = cfg.ApiKey;
			EnableRunScript = cfg.EnableRunScript;
			ExposeBepInExDocs = cfg.ExposeBepInExDocs;
			ExposeFullToolCatalog = cfg.ExposeFullToolCatalog;
			AllowImplicitDefaultSession = cfg.AllowImplicitDefaultSession;
			ImplicitDefaultSessionId = cfg.ImplicitDefaultSessionId;
			LogLevel = cfg.LogLevel;
			EnableFileLogging = cfg.EnableFileLogging;
			EnableOutputPaneLogging = cfg.EnableOutputPaneLogging;
			EnableToolCallLogging = cfg.EnableToolCallLogging;
		}

		void UpdateStatus() {
			StatusMessage = mcpServer.GetStatusMessage();
			OnPropertyChanged(nameof(IsServerRunning));
			OnPropertyChanged(nameof(PrimaryServerActionLabel));
		}

		void OnMessageLogged(object? sender, string message) {
			var dispatcher = System.Windows.Application.Current?.Dispatcher;
			if (dispatcher != null && !dispatcher.CheckAccess()) {
				dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
				return;
			}

			AppendLog(message);
		}

		void ResetLogs(IReadOnlyList<string> messages) {
			logEntries.Clear();
			var startIndex = messages.Count > MaxLogEntries ? messages.Count - MaxLogEntries : 0;
			for (int i = startIndex; i < messages.Count; i++)
				logEntries.Add(messages[i]);
			LogText = string.Join(Environment.NewLine, logEntries);
		}

		void AppendLog(string message) {
			logEntries.Add(message);
			while (logEntries.Count > MaxLogEntries)
				logEntries.RemoveAt(0);
			LogText = string.Join(Environment.NewLine, logEntries);
		}

		static void OpenInExplorer(string path, bool selectFile) {
			if (string.IsNullOrWhiteSpace(path))
				return;

			var target = path;
			if (!selectFile && !Directory.Exists(target))
				Directory.CreateDirectory(target);

			if (selectFile) {
				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
					Directory.CreateDirectory(directory);
				Process.Start(new ProcessStartInfo {
					FileName = "explorer.exe",
					Arguments = $"/select,\"{path}\"",
					UseShellExecute = true
				});
				return;
			}

			Process.Start(new ProcessStartInfo {
				FileName = target,
				UseShellExecute = true
			});
		}
	}
}
