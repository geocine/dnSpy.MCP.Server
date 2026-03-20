# dnSpy MCP Server

An embedded MCP server for dnSpy focused on .NET reconstruction, decompilation, reverse engineering, patchback, runtime inspection, and dnSpy host navigation.

> Forked from [chichicaste/dnSpy.MCP.Server](https://github.com/chichicaste/dnSpy.MCP.Server).

[Tool reference](./docs/tool-reference.md) | [Architecture](./docs/ARCHITECTURE.md) | [Changelog](./CHANGELOG.md)

## Requirements

- Windows
- .NET 10 SDK
- Git submodules enabled for the shared `hosts/dnSpy/` host checkout and the vendored dependencies under `vendors/`

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
# Default: repair/build dnSpy, then build and install both extensions
build.bat

# Build only the dnSpy host
build.bat dnspy

# Build only the MCP extension for net10.0-windows
build.bat mcp

# Build only the HoLLy extension for net10.0-windows
build.bat holly

# Update the HoLLy submodule to the latest origin/master
build.bat sync-holly

# Raw NUKE passthrough
build.bat nuke --target Mcp --verbosity verbose
```

Build outputs:

| Output | Path | Notes |
|---|---|---|
| Convenience root junction | `bin/` | Points to the host output root; created locally by the build, not committed |
| dnSpy host output root | `hosts/dnSpy/dnSpy/dnSpy/bin/Release/net10.0-windows/` | |
| Extension install root | `<dnSpy_host_output_root>/Extensions/` | |
| MCP extension | `<dnSpy_host_output_root>/Extensions/dnSpy.MCP.Server/` | |
| HoLLy extension | `<dnSpy_host_output_root>/Extensions/dnSpy.Extension.HoLLy/` | |
| Runtime mirrors | `<dnSpy_host_output_root>/win-x64/` and `win-x86/` | |
| de4dot payload | `artifacts/de4dotEx/net8/` | Build output, git-ignored |

## Run

Start the dnSpy host you built:

- launch `bin/dnSpy.exe`
- launch `hosts/dnSpy/dnSpy/dnSpy/bin/Release/net10.0-windows/dnSpy.exe`

- The MCP server starts automatically when **Enable server** is on.
- Host, port, logging, and related settings live in **Options -> MCP Server** and are persisted through `mcp-config.json`.
- de4dot support is built in from the vendored `vendors/de4dotEx/` submodule - there is no external `de4dot.exe` path to configure.

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

Origin breakdown: 180 total — 88 new, 10 enhanced, 82 upstream (carried from the original fork).

Upstream removals: 8 utility tools were intentionally dropped from the public surface, mainly skill-storage/config-management tools plus the external `run_de4dot` runner.

### Tool categories

| Category | Total | New | Enhanced | Upstream |
|---|---:|---:|---:|---:|
| [Bootstrap and discovery](./docs/tool-reference.md#bootstrap-and-discovery) | 15 | 14 | 1 | 0 |
| [Reconstruction core](./docs/tool-reference.md#reconstruction-core) | 19 | 13 | 1 | 5 |
| [Source and decompile](./docs/tool-reference.md#source-and-decompile) | 21 | 5 | 2 | 14 |
| [Architecture and xrefs](./docs/tool-reference.md#architecture-and-xrefs) | 20 | 11 | 0 | 9 |
| [Metadata and native analysis](./docs/tool-reference.md#metadata-and-native-analysis) | 13 | 11 | 1 | 1 |
| [Deobfuscation and recovery](./docs/tool-reference.md#deobfuscation-and-recovery) | 19 | 15 | 1 | 3 |
| [Provenance and correlation](./docs/tool-reference.md#provenance-and-correlation) | 8 | 8 | 0 | 0 |
| [Editing and patchback](./docs/tool-reference.md#editing-and-patchback) | 23 | 3 | 0 | 20 |
| [Debug runtime](./docs/tool-reference.md#debug-runtime) | 23 | 2 | 1 | 20 |
| [Memory and dumping](./docs/tool-reference.md#memory-and-dumping) | 10 | 0 | 1 | 9 |
| [UI navigation](./docs/tool-reference.md#ui-navigation) | 8 | 6 | 2 | 0 |
| [Scripting](./docs/tool-reference.md#scripting) | 1 | 0 | 0 | 1 |

## License

This project is licensed under [GPLv3](./LICENSE). See [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) for bundled dependency licenses.
