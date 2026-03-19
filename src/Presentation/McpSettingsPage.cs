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
using System.ComponentModel;
using System.ComponentModel.Composition;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Settings.Dialog;
using dnSpy.MCP.Server.Configuration;

namespace dnSpy.MCP.Server.Presentation {
	[Export(typeof(IAppSettingsPageProvider))]
	sealed class McpAppSettingsPageProvider : IAppSettingsPageProvider {
		readonly McpToolWindowViewModel sharedViewModel;

		[ImportingConstructor]
		McpAppSettingsPageProvider(McpToolWindowViewModel sharedViewModel) => this.sharedViewModel = sharedViewModel;

		public IEnumerable<AppSettingsPage> Create() {
			yield return new McpAppSettingsPage(sharedViewModel);
		}
	}

	sealed class McpAppSettingsPage : AppSettingsPage {
		internal static readonly Guid PageGuid = new Guid("68F555EB-A951-49C1-9708-C8756A5FAC39");

		readonly McpOptionsPageViewModel viewModel;
		McpSettingsControl? uiObject;

		public override Guid ParentGuid => Guid.Empty;
		public override Guid Guid => PageGuid;
		public override double Order => AppSettingsConstants.ORDER_DEBUGGER + 0.2;
		public override string Title => "MCP Server";
		public override object? UIObject => uiObject ??= new McpSettingsControl { DataContext = viewModel };

		public McpAppSettingsPage(McpToolWindowViewModel sharedViewModel) => viewModel = new McpOptionsPageViewModel(sharedViewModel);

		public override void OnApply() => viewModel.Apply();

		public override void OnClosed() => viewModel.Dispose();
	}

	sealed class McpOptionsPageViewModel : ViewModelBase, IMcpSettingsActionHandler, IDisposable {
		readonly McpToolWindowViewModel sharedViewModel;

		bool enableServer;
		string host = "localhost";
		int port = 3100;
		bool requireApiKey;
		string apiKey = string.Empty;
		bool enableRunScript;
		bool exposeFullToolCatalog;
		bool allowImplicitDefaultSession = true;
		string implicitDefaultSessionId = "__implicit_http_session__";
		string logLevel = "Info";
		bool enableFileLogging = true;
		bool enableOutputPaneLogging = true;
		bool enableToolCallLogging = true;

		public McpOptionsPageViewModel(McpToolWindowViewModel sharedViewModel) {
			this.sharedViewModel = sharedViewModel;
			LoadFrom(McpConfig.Instance);
			sharedViewModel.PropertyChanged += SharedViewModel_PropertyChanged;
		}

		public IEnumerable<string> AvailableLogLevels => sharedViewModel.AvailableLogLevels;
		public string ConfigFilePath => sharedViewModel.ConfigFilePath;
		public string LogFilePath => sharedViewModel.LogFilePath;
		public string StatusMessage => sharedViewModel.StatusMessage;
		public string LogText => sharedViewModel.LogText;
		public bool IsServerRunning => sharedViewModel.IsServerRunning;
		public string PrimaryServerActionLabel => sharedViewModel.PrimaryServerActionLabel;

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

		public void Apply() {
			CopyEditorStateToSharedViewModel();
			sharedViewModel.SaveConfiguration();
			LoadFrom(McpConfig.Instance);
		}

		public void ToggleServer() {
			CopyEditorStateToSharedViewModel();
			sharedViewModel.ToggleServer();
			LoadFrom(McpConfig.Instance);
		}

		public void StartServer() {
			CopyEditorStateToSharedViewModel();
			sharedViewModel.StartServer();
			LoadFrom(McpConfig.Instance);
		}

		public void StopServer() {
			CopyEditorStateToSharedViewModel();
			sharedViewModel.StopServer();
			LoadFrom(McpConfig.Instance);
		}

		public void RestartServer() {
			CopyEditorStateToSharedViewModel();
			sharedViewModel.RestartServer();
			LoadFrom(McpConfig.Instance);
		}

		public void SaveConfiguration() => Apply();

		public void ReloadConfiguration() {
			sharedViewModel.ReloadConfiguration();
			LoadFrom(McpConfig.Instance);
		}

		public void OpenConfigFile() => sharedViewModel.OpenConfigFile();
		public void OpenLogFile() => sharedViewModel.OpenLogFile();
		public void OpenLogDirectory() => sharedViewModel.OpenLogDirectory();

		public void ClearLogs() {
			sharedViewModel.ClearLogs();
			OnPropertyChanged(nameof(LogText));
		}

		public void Dispose() => sharedViewModel.PropertyChanged -= SharedViewModel_PropertyChanged;

		void LoadFrom(McpConfig cfg) {
			EnableServer = cfg.EnableServer;
			Host = cfg.Host;
			Port = cfg.Port;
			RequireApiKey = cfg.RequireApiKey;
			ApiKey = cfg.ApiKey;
			EnableRunScript = cfg.EnableRunScript;
			ExposeFullToolCatalog = cfg.ExposeFullToolCatalog;
			AllowImplicitDefaultSession = cfg.AllowImplicitDefaultSession;
			ImplicitDefaultSessionId = cfg.ImplicitDefaultSessionId;
			LogLevel = cfg.LogLevel;
			EnableFileLogging = cfg.EnableFileLogging;
			EnableOutputPaneLogging = cfg.EnableOutputPaneLogging;
			EnableToolCallLogging = cfg.EnableToolCallLogging;
		}

		void CopyEditorStateToSharedViewModel() {
			sharedViewModel.EnableServer = EnableServer;
			sharedViewModel.Host = Host;
			sharedViewModel.Port = Port;
			sharedViewModel.RequireApiKey = RequireApiKey;
			sharedViewModel.ApiKey = ApiKey;
			sharedViewModel.EnableRunScript = EnableRunScript;
			sharedViewModel.ExposeFullToolCatalog = ExposeFullToolCatalog;
			sharedViewModel.AllowImplicitDefaultSession = AllowImplicitDefaultSession;
			sharedViewModel.ImplicitDefaultSessionId = ImplicitDefaultSessionId;
			sharedViewModel.LogLevel = LogLevel;
			sharedViewModel.EnableFileLogging = EnableFileLogging;
			sharedViewModel.EnableOutputPaneLogging = EnableOutputPaneLogging;
			sharedViewModel.EnableToolCallLogging = EnableToolCallLogging;
		}

		void SharedViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName == nameof(McpToolWindowViewModel.StatusMessage))
				OnPropertyChanged(nameof(StatusMessage));
			else if (e.PropertyName == nameof(McpToolWindowViewModel.IsServerRunning))
				OnPropertyChanged(nameof(IsServerRunning));
			else if (e.PropertyName == nameof(McpToolWindowViewModel.PrimaryServerActionLabel))
				OnPropertyChanged(nameof(PrimaryServerActionLabel));
			else if (e.PropertyName == nameof(McpToolWindowViewModel.LogText))
				OnPropertyChanged(nameof(LogText));
		}
	}
}
