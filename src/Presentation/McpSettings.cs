/*
    Copyright (C) 2026 @chichicaste

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
using System.ComponentModel;
using System.ComponentModel.Composition;
using dnSpy.Contracts.MVVM;
using dnSpy.MCP.Server.Communication;
using dnSpy.MCP.Server.Configuration;
using dnSpy.MCP.Server.Helper;

namespace dnSpy.MCP.Server.Presentation {
	/// <summary>
	/// Runtime settings for the MCP server extension.
	/// Values are hydrated from mcp-config.json and kept in sync with it.
	/// </summary>
	public class McpSettings : ViewModelBase {
		/// <summary>
		/// Allows derived implementations to attach a server instance so lifecycle changes can be propagated.
		/// Base implementation is a no-op.
		/// </summary>
		/// <param name="server">The MCP server instance to attach.</param>
		internal virtual void SetServer(McpServer server) {
			// Default implementation intentionally left blank.
		}

		/// <summary>
		/// Gets or sets whether the MCP server is enabled.
		/// </summary>
		public bool EnableServer {
			get => enableServer;
			set {
				if (enableServer != value) {
					enableServer = value;
					OnPropertyChanged(nameof(EnableServer));
				}
			}
		}
		bool enableServer = true;

		/// <summary>
		/// Gets or sets the server host (default: localhost).
		/// </summary>
		public string Host {
			get => host;
			set {
				if (host != value) {
					host = value;
					OnPropertyChanged(nameof(Host));
				}
			}
		}
		string host = "localhost";

		/// <summary>
		/// Gets or sets the server port (default: 3100 - to avoid conflicts with Docker/Node).
		/// </summary>
		public int Port {
			get => port;
			set {
				if (port != value) {
					port = value;
					OnPropertyChanged(nameof(Port));
				}
			}
		}
		int port = 3100;

		/// <summary>
		/// Log an informational message.
		/// </summary>
		public void Log(string message) => McpLogger.Info(message);

		/// <summary>
		/// Log an informational message.
		/// </summary>
		public void LogInfo(string message) => McpLogger.Info(message);

		/// <summary>
		/// Log a warning message.
		/// </summary>
		public void LogWarn(string message) => McpLogger.Warning(message);

		/// <summary>
		/// Log an error message.
		/// </summary>
		public void LogError(string message) => McpLogger.Error(message);

		public void ClearLogs() => McpLogger.ClearInMemoryMessages();

		/// <summary>
		/// Creates a copy of these settings.
		/// </summary>
		public McpSettings Clone() => CopyTo(new McpSettings());

		/// <summary>
		/// Copies these settings to another instance.
		/// </summary>
		public McpSettings CopyTo(McpSettings other) {
			other.EnableServer = EnableServer;
			other.Host = Host;
			other.Port = Port;
			return other;
		}
	}

	/// <summary>
	/// Implementation of MCP settings with persistence support.
	/// </summary>
	[Export(typeof(McpSettings))]
	sealed class McpSettingsImpl : McpSettings {
		McpServer? mcpServer;

		[ImportingConstructor]
		McpSettingsImpl() {
			var cfg = McpConfig.Instance;
			EnableServer = cfg.EnableServer;
			Host = cfg.Host;
			Port = cfg.Port;
			PropertyChanged += McpSettingsImpl_PropertyChanged;
		}

		/// <summary>
		/// Stores the server reference so that property-change events (Enable/Disable toggle)
		/// can control the server lifecycle at runtime.
		/// The initial start is deferred to the AppLoaded event in TheExtension so that
		/// all MEF services and documents are fully initialized before the server begins
		/// accepting connections.
		/// </summary>
		internal override void SetServer(McpServer server) {
			mcpServer = server;
		}

		void McpSettingsImpl_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			PersistRuntimeSettings();

			if (e.PropertyName != nameof(EnableServer) || mcpServer == null)
				return;

			if (EnableServer) {
				Log("Starting MCP server");
				mcpServer.Start();
				VerifyServerStateAsync(expectedRunning: true);
			}
			else {
				Log("Stopping MCP server");
				mcpServer.Stop();
				VerifyServerStateAsync(expectedRunning: false);
			}
		}

		void PersistRuntimeSettings() {
			try {
				var cfg = McpConfig.Instance;
				cfg.EnableServer = EnableServer;
				cfg.Host = Host;
				cfg.Port = Port;
				cfg.Save();
			}
			catch (Exception ex) {
				McpLogger.Exception(ex, "Failed to persist MCP runtime settings");
			}
		}

		void VerifyServerStateAsync(bool expectedRunning) {
			System.Threading.Tasks.Task.Run(async () => {
				await System.Threading.Tasks.Task.Delay(expectedRunning ? 300 : 200);
				try {
					if (mcpServer == null)
						return;

					if (expectedRunning)
						Log(mcpServer.IsRunning ? "MCP server is running" : "MCP server failed to start");
					else
						Log(mcpServer.IsRunning ? "MCP server is still running" : "MCP server stopped");
				}
				catch (Exception ex) {
					Log($"Error checking server status: {ex.Message}");
				}
			});
		}
	}
}
