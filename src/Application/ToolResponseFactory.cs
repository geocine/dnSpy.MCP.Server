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
using System.Text.Json;
using dnSpy.MCP.Server.Contracts;

namespace dnSpy.MCP.Server.Application {
	internal static class ToolResponseFactory {
		static readonly JsonSerializerOptions indentedJson = new JsonSerializerOptions {
			WriteIndented = true
		};

		public static CallToolResult Text(string text, bool isError = false, object? structuredContent = null) {
			return new CallToolResult {
				Content = new List<ToolContent> {
					new ToolContent {
						Text = text
					}
				},
				IsError = isError,
				StructuredContent = structuredContent
			};
		}

		public static CallToolResult Json(object structuredContent, bool isError = false) {
			return new CallToolResult {
				Content = new List<ToolContent> {
					new ToolContent {
						Text = JsonSerializer.Serialize(structuredContent, indentedJson)
					}
				},
				IsError = isError,
				StructuredContent = structuredContent
			};
		}
	}
}
