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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using dnSpy.MCP.Server.Configuration;
using dnSpy.MCP.Server.Contracts;
using dnSpy.MCP.Server.Presentation;

namespace dnSpy.MCP.Server.Communication {
	/// <summary>
	/// Provides embedded BepInEx documentation resources for MCP when enabled in config.
	/// </summary>
	[Export(typeof(BepInExResources))]
	public sealed class BepInExResources {
		const string ResourcePrefix = "dnSpy.MCP.Server.Resources.BepInEx.";
		const string ResourceUriPrefix = "bepinex://docs/";

		readonly McpSettings settings;
		readonly Dictionary<string, string> resources;

		[ImportingConstructor]
		public BepInExResources(McpSettings settings) {
			this.settings = settings;
			resources = LoadEmbeddedResources();
		}

		public List<ResourceInfo> GetResources() {
			if (!McpConfig.Instance.ExposeBepInExDocs)
				return new List<ResourceInfo>();

			return resources
				.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
				.Select(kvp => {
					var name = kvp.Key.Substring(ResourceUriPrefix.Length);
					return new ResourceInfo {
						Uri = kvp.Key,
						Name = name,
						Description = $"BepInEx v6 Documentation: {FormatName(name)}",
						MimeType = "text/markdown"
					};
				})
				.ToList();
		}

		public string? ReadResource(string uri) {
			if (!McpConfig.Instance.ExposeBepInExDocs)
				return null;

			return resources.TryGetValue(uri, out var content) ? content : null;
		}

		Dictionary<string, string> LoadEmbeddedResources() {
			var assembly = typeof(BepInExResources).Assembly;
			var resourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var resourceName in assembly.GetManifestResourceNames()
				.Where(IsBepInExDocResource)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)) {
				var slug = Path.GetFileNameWithoutExtension(resourceName.Substring(ResourcePrefix.Length));
				var uri = $"{ResourceUriPrefix}{slug}";
				resourceMap[uri] = ReadEmbeddedMarkdown(assembly, resourceName);
			}

			if (resourceMap.Count == 0)
				settings.LogWarn("No embedded BepInEx documentation resources were found.");
			else
				settings.Log($"Loaded {resourceMap.Count} embedded BepInEx documentation resources");

			return resourceMap;
		}

		static bool IsBepInExDocResource(string resourceName) =>
			resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
			resourceName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

		static string ReadEmbeddedMarkdown(Assembly assembly, string resourceName) {
			using var stream = assembly.GetManifestResourceStream(resourceName)
				?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
			using var reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}

		static string FormatName(string name) {
			return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
				name.Replace('-', ' ')
			);
		}
	}
}
