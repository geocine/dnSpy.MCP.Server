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

using System.ComponentModel.Composition;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Menus;
using dnSpy.Contracts.Settings.Dialog;
using dnSpy.MCP.Server.Communication;

namespace dnSpy.MCP.Server.Presentation {
	static class McpMenuConstants {
		public const string AppMenuMcpGuid = "A7A6C1C2-57E5-4987-B9C8-3F5397B93D8E";
		public const string GroupAppMenuMcpSettings = "0,8C91E34A-8A34-42D0-930F-FAFB3A64F596";
		public const string GroupAppMenuMcpServer = "1000,BBFF7D28-6C5B-4867-83B1-9A997514AE16";
		public const string GroupAppMenuMcpConfig = "2000,7D27D65A-40EF-477E-842B-0242B18440B1";
	}

	[ExportMenu(OwnerGuid = MenuConstants.APP_MENU_GUID, Guid = McpMenuConstants.AppMenuMcpGuid, Order = MenuConstants.ORDER_APP_MENU_DEBUG + 10, Header = "MCP")]
	sealed class McpAppMenu : IMenu { }

	[ExportMenuItem(OwnerGuid = McpMenuConstants.AppMenuMcpGuid, Header = "MCP Server Options", Icon = DsImagesAttribute.Settings, Group = McpMenuConstants.GroupAppMenuMcpSettings, Order = 0)]
	sealed class ShowMcpOptionsCommand : MenuItemBase {
		readonly IAppSettingsService appSettingsService;

		[ImportingConstructor]
		public ShowMcpOptionsCommand(IAppSettingsService appSettingsService) => this.appSettingsService = appSettingsService;

		public override void Execute(IMenuItemContext context) => appSettingsService.Show(McpAppSettingsPage.PageGuid);
	}

	[ExportMenuItem(OwnerGuid = McpMenuConstants.AppMenuMcpGuid, Header = "Start MCP Server", Icon = DsImagesAttribute.RunOutline, Group = McpMenuConstants.GroupAppMenuMcpServer, Order = 0)]
	sealed class StartMcpServerFromMcpMenuCommand : MenuItemBase {
		readonly McpToolWindowViewModel viewModel;
		readonly McpServer mcpServer;

		[ImportingConstructor]
		public StartMcpServerFromMcpMenuCommand(McpToolWindowViewModel viewModel, McpServer mcpServer) {
			this.viewModel = viewModel;
			this.mcpServer = mcpServer;
		}

		public override bool IsEnabled(IMenuItemContext context) => !mcpServer.IsRunning;
		public override void Execute(IMenuItemContext context) => viewModel.StartServer();
	}

	[ExportMenuItem(OwnerGuid = McpMenuConstants.AppMenuMcpGuid, Header = "Stop MCP Server", Icon = DsImagesAttribute.Stop, Group = McpMenuConstants.GroupAppMenuMcpServer, Order = 10)]
	sealed class StopMcpServerFromMcpMenuCommand : MenuItemBase {
		readonly McpToolWindowViewModel viewModel;
		readonly McpServer mcpServer;

		[ImportingConstructor]
		public StopMcpServerFromMcpMenuCommand(McpToolWindowViewModel viewModel, McpServer mcpServer) {
			this.viewModel = viewModel;
			this.mcpServer = mcpServer;
		}

		public override bool IsEnabled(IMenuItemContext context) => mcpServer.IsRunning;
		public override void Execute(IMenuItemContext context) => viewModel.StopServer();
	}

	[ExportMenuItem(OwnerGuid = McpMenuConstants.AppMenuMcpGuid, Header = "Restart MCP Server", Icon = DsImagesAttribute.Refresh, Group = McpMenuConstants.GroupAppMenuMcpServer, Order = 20)]
	sealed class RestartMcpServerFromMcpMenuCommand : MenuItemBase {
		readonly McpToolWindowViewModel viewModel;

		[ImportingConstructor]
		public RestartMcpServerFromMcpMenuCommand(McpToolWindowViewModel viewModel) => this.viewModel = viewModel;

		public override void Execute(IMenuItemContext context) => viewModel.RestartServer();
	}

	[ExportMenuItem(OwnerGuid = McpMenuConstants.AppMenuMcpGuid, Header = "Open Config File", Icon = DsImagesAttribute.OpenFolder, Group = McpMenuConstants.GroupAppMenuMcpConfig, Order = 0)]
	sealed class OpenMcpConfigFileCommand : MenuItemBase {
		readonly McpToolWindowViewModel viewModel;

		[ImportingConstructor]
		public OpenMcpConfigFileCommand(McpToolWindowViewModel viewModel) => this.viewModel = viewModel;

		public override void Execute(IMenuItemContext context) => viewModel.OpenConfigFile();
	}
}
