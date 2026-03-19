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
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace dnSpy.MCP.Server.Configuration
{
    /// <summary>
    /// Loads and holds settings from mcp-config.json, which lives alongside the
    /// MCP server DLL in the dnSpy output directory.
    /// The file is created with defaults on first use if it does not exist.
    /// </summary>
    public sealed class McpConfig
    {
        static McpConfig? _instance;
        static readonly object _lock = new object();

        // ── Properties ───────────────────────────────────────────────────────

        /// <summary>
        /// If true, the MCP server should start automatically and stay enabled.
        /// This is the primary on/off switch exposed by the dedicated MCP UI.
        /// </summary>
        [JsonPropertyName("enableServer")]
        public bool EnableServer { get; set; } = true;

        /// <summary>
        /// Absolute path to de4dot.exe.  Leave empty to use auto-discovery.
        /// Example: "C:/tools/de4dot/de4dot.exe"
        /// </summary>
        [JsonPropertyName("de4dotExePath")]
        public string De4dotExePath { get; set; } = "";

        /// <summary>
        /// Extra directories to search for de4dot.exe when de4dotExePath is empty.
        /// Paths are tried in order; relative paths are resolved from the config file's directory.
        /// </summary>
        [JsonPropertyName("de4dotSearchPaths")]
        public List<string> De4dotSearchPaths { get; set; } = new List<string>();

        /// <summary>
        /// Hostname or IP address the MCP server listens on.
        /// Use "localhost" (default) for local-only access.
        /// Use "0.0.0.0" or "+" to listen on all interfaces — required for remote debugging
        /// from a sandbox or virtual machine. Note: non-localhost bindings require a prior
        /// netsh url reservation: netsh http add urlacl url=http://+:PORT/ user=Everyone
        /// </summary>
        [JsonPropertyName("host")]
        public string Host { get; set; } = "localhost";

        /// <summary>TCP port the MCP server listens on. Default 3100.</summary>
        [JsonPropertyName("port")]
        public int Port { get; set; } = 3100;

        /// <summary>If true, all requests must include X-API-Key or Authorization: Bearer <ApiKey>.</summary>
        [JsonPropertyName("requireApiKey")]
        public bool RequireApiKey { get; set; } = false;

        /// <summary>API key value. Generate with: openssl rand -hex 32</summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// Enables the run_script tool. Default false — disable when analyzing malware.
        /// Set to true only in trusted environments.
        /// </summary>
        [JsonPropertyName("enableRunScript")]
        public bool EnableRunScript { get; set; } = false;

        /// <summary>
        /// If true, MCP tools/list exposes the full tool catalog for compatibility with older clients.
        /// If false (default), tools/list exposes only the bootstrap discovery/code-mode surface.
        /// </summary>
        [JsonPropertyName("exposeFullToolCatalog")]
        public bool ExposeFullToolCatalog { get; set; } = false;

        /// <summary>
        /// If true (default), stateless HTTP clients that do not have an SSE session can still
        /// enable tool groups and build up a working discovery context through a shared implicit session.
        /// This makes staged discovery work with clients such as Codex that primarily use direct tools/call.
        /// </summary>
        [JsonPropertyName("allowImplicitDefaultSession")]
        public bool AllowImplicitDefaultSession { get; set; } = true;

        /// <summary>
        /// Session id used when allowImplicitDefaultSession is true and no explicit session id is supplied.
        /// </summary>
        [JsonPropertyName("implicitDefaultSessionId")]
        public string ImplicitDefaultSessionId { get; set; } = "__implicit_http_session__";

        /// <summary>
        /// Minimum log level written by the centralized logger. One of: Debug, Info, Warning, Error.
        /// Default Info.
        /// </summary>
        [JsonPropertyName("logLevel")]
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// If true, write MCP logs to the dnspy_mcp.log file. Default true.
        /// </summary>
        [JsonPropertyName("enableFileLogging")]
        public bool EnableFileLogging { get; set; } = true;

        /// <summary>
        /// If true, write MCP logs to the dnSpy output pane. Default true.
        /// </summary>
        [JsonPropertyName("enableOutputPaneLogging")]
        public bool EnableOutputPaneLogging { get; set; } = true;

        /// <summary>
        /// If true, emit structured per-tool execution telemetry. Default true.
        /// </summary>
        [JsonPropertyName("enableToolCallLogging")]
        public bool EnableToolCallLogging { get; set; } = true;

        /// <summary>
        /// Maximum directory levels to search upward for a sibling de4dot repository when
        /// de4dotExePath is empty and the exe is not found next to the DLL.
        /// Default 6. Increase if your repo is nested deeper.
        /// </summary>
        [JsonPropertyName("de4dotMaxSearchDepth")]
        public int De4dotMaxSearchDepth { get; set; } = 6;

        // ── Singleton ─────────────────────────────────────────────────────────

        public static McpConfig Instance
        {
            get
            {
                if (_instance == null)
                    lock (_lock)
                        if (_instance == null)
                            _instance = Load();
                return _instance;
            }
        }

        /// <summary>Re-reads the config file and replaces the cached instance.</summary>
        public static McpConfig Reload()
        {
            lock (_lock)
                _instance = Load();
            return _instance;
        }

        // ── File location ─────────────────────────────────────────────────────

        /// <summary>
        /// Absolute path to the config file (next to the MCP server DLL).
        /// </summary>
        public static string ConfigFilePath
        {
            get
            {
                var dllDir = Path.GetDirectoryName(
                    typeof(McpConfig).Assembly.Location
                    ?? Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
                return Path.Combine(dllDir, "mcp-config.json");
            }
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        static McpConfig Load()
        {
            var path = ConfigFilePath;
            McpConfig cfg;

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    cfg = JsonSerializer.Deserialize<McpConfig>(json,
                        new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })
                        ?? new McpConfig();
                }
                catch
                {
                    cfg = new McpConfig();
                }
            }
            else
            {
                cfg = new McpConfig();
                // Write a template so the user knows what to configure
                try { cfg.Save(path); } catch { }
            }

            return cfg;
        }

        void Save(string path)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Persists the current in-memory configuration to the standard config file path.
        /// </summary>
        public void Save()
        {
            Save(ConfigFilePath);
        }

        // ── de4dot resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Resolves the path to de4dot.exe using config + well-known fallbacks.
        /// Returns null if de4dot cannot be found.
        /// </summary>
        public string? ResolveDe4dotExe()
        {
            // 1. Explicit path in config
            if (!string.IsNullOrEmpty(De4dotExePath) && File.Exists(De4dotExePath))
                return De4dotExePath;

            var configDir = Path.GetDirectoryName(ConfigFilePath) ?? "";

            // 2. User-supplied search paths (config-relative or absolute)
            foreach (var raw in De4dotSearchPaths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var p = Path.IsPathRooted(raw) ? raw : Path.GetFullPath(Path.Combine(configDir, raw));
                if (File.Exists(p)) return p;
            }

            // 3. de4dot.exe in the same directory as the DLL
            var nextToDll = Path.Combine(configDir, "de4dot.exe");
            if (File.Exists(nextToDll)) return nextToDll;

            // 4. Sibling de4dot repo (dev environment heuristic):
            //    DLL lives at <repo>/dnSpy/dnSpy/bin/<config>/<tfm>/
            //    de4dot lives at <parent-of-repo>/../de4dot/Debug/net48/
            try
            {
                // Walk up to find the repo root (contains .git or dnSpy subdir)
                var dir = configDir;
                int depth = De4dotMaxSearchDepth > 0 ? De4dotMaxSearchDepth : 6;
                for (int i = 0; i < depth && !string.IsNullOrEmpty(dir); i++)
                {
                    var candidate = Path.GetFullPath(Path.Combine(dir, "..", "de4dot", "Debug", "net48", "de4dot.exe"));
                    if (File.Exists(candidate)) return candidate;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }

            // 5. Same path structure but on common Windows drive letters (D:\, E:\, etc.)
            //    for cases where repos are on a different drive than the running binary
            try
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    // Try <USERPROFILE>\source\repos\de4dot\Debug\net48\de4dot.exe
                    var reposBase = Path.Combine(userProfile, "source", "repos");
                    var candidate = Path.Combine(reposBase, "de4dot", "Debug", "net48", "de4dot.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            return null;
        }
    }
}
