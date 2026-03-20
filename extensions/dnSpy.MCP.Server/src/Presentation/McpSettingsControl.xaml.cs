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

using System.Windows;
using System.Windows.Controls;

namespace dnSpy.MCP.Server.Presentation {
	/// <summary>
	/// Shared MCP settings UI used by the options page.
	/// </summary>
	public partial class McpSettingsControl : UserControl {
		/// <summary>
		/// Initializes the control.
		/// </summary>
		public McpSettingsControl() => InitializeComponent();

		IMcpSettingsActionHandler? ViewModel => DataContext as IMcpSettingsActionHandler;

		void PrimaryServerActionButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.ToggleServer();
		void SaveButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.SaveConfiguration();
		void ReloadButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.ReloadConfiguration();
		void OpenConfigButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.OpenConfigFile();
		void OpenLogFileButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.OpenLogFile();
		void OpenLogFolderButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.OpenLogDirectory();
		void ClearLogsButton_OnClick(object sender, RoutedEventArgs e) => ViewModel?.ClearLogs();

		void LogTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => LogTextBox.ScrollToEnd();
	}
}
