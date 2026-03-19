# dnSpy MCP Server

An embedded MCP server for dnSpy focused on .NET reconstruction, decompilation, reverse engineering, patchback, runtime inspection, and dnSpy host navigation.

> Forked from [chichicaste/dnSpy.MCP.Server](https://github.com/chichicaste/dnSpy.MCP.Server).

## [Tool reference](./docs/tool-reference.md) | [Architecture](./docs/ARCHITECTURE.md) | [Changelog](./CHANGELOG.md)

## Requirements

- Windows
- .NET 10 SDK
- Git submodules enabled for the vendored `dnSpy/` and `de4dotEx/` checkouts

## Getting started

Clone the repo with submodules:

```bash
git clone --recursive https://github.com/geocine/dnSpy.MCP.Server.git
cd dnSpy.MCP.Server
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Build

Use `build.bat`. It is the intended entry point and wraps the NUKE build.

```bash
# Default: repair/build dnSpy, build the de4dotEx submodule payload,
# then build the MCP extension for net10.0-windows
build.bat

# Build only the dnSpy host
build.bat dnspy

# Build only the MCP extension for net10.0-windows
build.bat mcp

# Raw NUKE passthrough
build.bat nuke --target McpNet10 --verbosity verbose
```

Build outputs:

- Extension output root: `dnSpy/dnSpy/dnSpy/bin/Release/net10.0-windows/`
- `net10.0-windows` runtime copies: `dnSpy/dnSpy/dnSpy/bin/Release/net10.0-windows/win-x64/` and `win-x86/`
- Main extension assembly: `dnSpy.MCP.Server.x.dll`
- Default config deployed next to the extension: `mcp-config.json`
- Generated de4dot payload: `libs/de4dot-net8/` (build output, git-ignored)

## Run

Start the dnSpy host you built:

- launch `dnSpy/dnSpy/dnSpy/bin/Release/net10.0-windows/dnSpy.exe`

The MCP server starts automatically when `Enable server` is on. Host, port, logging, and related settings are available in `Options -> MCP Server` and persisted through `mcp-config.json`. de4dot support is built in from the vendored `de4dotEx/` submodule; there is no external `de4dot.exe` path to configure.

Default HTTP endpoints:

- MCP: `http://localhost:3100/mcp`
- Health: `http://localhost:3100/health`
- Metadata: `http://localhost:3100/`

Quick health check:

```bash
curl http://localhost:3100/health
```

## MCP client configuration

The primary transport is streamable HTTP on `POST /mcp`. Legacy SSE endpoints are still available for older clients, but the intended endpoint for current MCP clients is `/mcp`.

Standard config:

```json
{
  "mcpServers": {
    "dnspy": {
      "url": "http://localhost:3100/mcp"
    }
  }
}
```

### Codex

Codex can use the server directly over HTTP.

CLI:

```bash
codex mcp add dnspy --url http://localhost:3100/mcp
```

Config file:

```toml
[mcp_servers.dnspy]
url = "http://localhost:3100/mcp"
```

### Claude Code

Claude Code should be configured as a remote HTTP MCP server.

CLI:

```bash
claude mcp add --transport http dnspy http://localhost:3100/mcp
```

Project config:

```json
{
  "mcpServers": {
    "dnspy": {
      "type": "http",
      "url": "http://localhost:3100/mcp"
    }
  }
}
```

## Tool reference

The exhaustive tool list lives in [docs/tool-reference.md](./docs/tool-reference.md).

### Tool categories

- [Bootstrap and discovery](./docs/tool-reference.md#bootstrap-and-discovery)
- [Reconstruction core](./docs/tool-reference.md#reconstruction-core)
- [Source and decompile](./docs/tool-reference.md#source-and-decompile)
- [Architecture and xrefs](./docs/tool-reference.md#architecture-and-xrefs)
- [Metadata and native analysis](./docs/tool-reference.md#metadata-and-native-analysis)
- [Deobfuscation and recovery](./docs/tool-reference.md#deobfuscation-and-recovery)
- [Provenance and correlation](./docs/tool-reference.md#provenance-and-correlation)
- [Editing and patchback](./docs/tool-reference.md#editing-and-patchback)
- [Debug runtime](./docs/tool-reference.md#debug-runtime)
- [Memory and dumping](./docs/tool-reference.md#memory-and-dumping)
- [UI navigation](./docs/tool-reference.md#ui-navigation)
- [Scripting](./docs/tool-reference.md#scripting)
- [Legacy compatibility](./docs/tool-reference.md#legacy-compatibility)
