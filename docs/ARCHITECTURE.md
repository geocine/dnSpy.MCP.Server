# dnSpy MCP Server Architecture

This document describes the current v2 architecture of the dnSpy MCP Server as implemented in this repository.

## Overview

The server is an in-process dnSpy extension that exposes dnSpy analysis, editing, debugging, and host-navigation capabilities over MCP.

Current architectural direction:

- Primary transport is streamable HTTP on `POST /mcp`
- Legacy SSE remains available for older clients
- Public tool names are standardized to `dnspy_*`
- `tools/list` is bootstrap-first by default
- Larger workflow areas are unlocked per session through tool groups
- Code mode is the preferred path for multi-step reconstruction work

## Runtime model

At runtime the extension is loaded by dnSpy through MEF. The extension starts an `HttpListener`-based MCP server, exposes a public tool catalog, and bridges MCP calls to dnSpy services and dnlib-based analysis code inside the same process.

High-level flow:

1. dnSpy loads the extension and composes exported services.
2. `TheExtension` starts the MCP server when MCP is enabled.
3. `McpServer` accepts HTTP requests on the configured host and port.
4. MCP lifecycle and tool requests are routed to `McpTools`.
5. `McpTools` resolves the public `dnspy_*` name to the internal implementation name.
6. Discovery rules decide whether the tool is visible for the current session.
7. The request is executed either by inline logic or by delegating to a domain service such as `AssemblyTools`, `DebugTools`, or `EditTools`.
8. Results are normalized through `ToolResponseFactory` and returned as MCP tool responses.

## Transports and endpoints

The server implementation is in [src/Communication/McpServer.cs](../src/Communication/McpServer.cs).

Current endpoint shape:

- `POST /mcp` - primary streamable HTTP MCP endpoint
- `GET /mcp` - SSE-compatible connection path for legacy clients
- `GET /sse` and `GET /events` - legacy SSE endpoints
- `POST /message` and `POST /messages` - legacy SSE message endpoints
- `GET /health`, `GET /healthz`, `GET /readyz` - health endpoints
- `GET /` - endpoint metadata

Important transport behavior:

- Streamable HTTP is the intended transport for modern clients.
- Legacy SSE is still kept for compatibility.
- The listener can fall forward to the next available port if the configured port is occupied.
- Host values such as `0.0.0.0` and `*` are translated to the `HttpListener` wildcard form.

## Core server components

### `McpServer`

File: [src/Communication/McpServer.cs](../src/Communication/McpServer.cs)

Responsibilities:

- start and stop the listener
- enforce authorization and origin checks
- serve streamable HTTP and legacy SSE transports
- handle MCP lifecycle methods such as `initialize`, `tools/list`, and `tools/call`
- expose health and metadata endpoints
- bridge session-aware tool listing to `McpTools`

Key point:

- the server is no longer best described as "SSE-first"; streamable HTTP is now the primary transport

### `McpTools`

Files:

- [src/Application/McpTools.cs](../src/Application/McpTools.cs)
- [src/Application/McpTools.Schemas.cs](../src/Application/McpTools.Schemas.cs)
- [src/Application/McpTools.Discovery.cs](../src/Application/McpTools.Discovery.cs)

Responsibilities:

- execute tools
- define tool schemas and descriptions
- implement bootstrap discovery behavior
- maintain per-session enabled tool groups
- expose code mode guidance, examples, and validation
- support hidden-tool access from `dnspy_execute_code` when appropriate

Split of responsibilities:

- `McpTools.cs` contains execution routing, inline helpers, and lazy delegation
- `McpTools.Schemas.cs` defines the tool schemas that become the public MCP surface
- `McpTools.Discovery.cs` implements bootstrap-first listing, tool groups, and session visibility rules

### `ToolCatalog` and public naming

File: [src/Application/ToolCatalog.cs](../src/Application/ToolCatalog.cs)

Responsibilities:

- attach metadata such as group, tags, and read-only/destructive hints
- map internal tool names to public `dnspy_*` names
- rewrite tool descriptions and schema text so public names are consistent
- define workflow groups such as `bootstrap`, `reconstruction_core`, `ui_navigation`, and `debug_runtime`

Important naming behavior:

- most internal tools become `dnspy_<internal_name>`
- selected tools use overrides, for example `find_who_calls_method` becomes `dnspy_find_callers`

### `ToolResponseFactory`

File: [src/Application/ToolResponseFactory.cs](../src/Application/ToolResponseFactory.cs)

Responsibilities:

- normalize text and JSON responses
- populate structured content consistently
- keep tool output formatting uniform across inline and delegated commands

## Discovery model

The v2 server uses a staged discovery model rather than exposing the entire catalog by default.

Implementation:

- [src/Application/McpTools.Discovery.cs](../src/Application/McpTools.Discovery.cs)
- [src/Application/ToolCatalog.cs](../src/Application/ToolCatalog.cs)

Default behavior:

- `tools/list` returns a narrow bootstrap surface first
- bootstrap tools include code-mode helpers, search/schema helpers, and tool-group controls
- non-bootstrap tools are hidden until their group is enabled for the session
- if `ExposeFullToolCatalog` is enabled in config, the full catalog becomes visible

Session behavior:

- tool-group enablement is tracked per session
- stateless HTTP clients can use the implicit default session when that config is enabled
- hidden tools return a structured "tool not enabled" error with the required group and suggested next steps

Code mode behavior:

- `dnspy_execute_code` is intended as the preferred orchestration surface for multi-step work
- code mode can discover schemas, search tools, and call hidden tools internally without widening the public tool list
- `dnspy_execute_code` and `dnspy_run_script` are intentionally blocked from recursively calling themselves from code mode

## Domain services

Most actual reverse-engineering work is implemented in domain-focused services that `McpTools` loads lazily.

### `AssemblyTools`

File: [src/Application/AssemblyTools.cs](../src/Application/AssemblyTools.cs)

Responsibilities:

- assembly discovery and selection
- reconstruction helpers such as startup/resource maps
- decompiled source extraction and project export
- metadata and native-inspection helpers
- provenance and correlation workflows
- many reconstruction-first v2 additions

### `TypeTools`

File: [src/Application/TypeTools.cs](../src/Application/TypeTools.cs)

Responsibilities:

- type and member inspection
- method decompilation
- IL and exception handler inspection
- path-to-type analysis and related type exploration

### `EditTools`

File: [src/Application/EditTools.cs](../src/Application/EditTools.cs)

Responsibilities:

- rename and patchback workflows
- assembly metadata editing
- resource CRUD and Costura extraction
- assembly reference editing
- method patching and type injection

Note:

- resource operations are now part of `EditTools`; there is no separate `ResourceTools` service in the current architecture

### `DebugTools`

File: [src/Application/DebugTools.cs](../src/Application/DebugTools.cs)

Responsibilities:

- debugger lifecycle and breakpoint control
- stepping, stack, and frame inspection
- runtime context helpers such as `inspect_breakpoint`
- dnSpy host navigation tools such as `select_document_node`, `follow_reference`, `get_tool_window_state`, and `focus_debugger_context`

### `DumpTools`

File: [src/Application/DumpTools.cs](../src/Application/DumpTools.cs)

Responsibilities:

- memory dumping
- PE section extraction
- raw process-memory read and write
- unpack-from-memory workflows
- CorDebug IL dumping

### `MemoryInspectTools`

File: [src/Application/MemoryInspectTools.cs](../src/Application/MemoryInspectTools.cs)

Responsibilities:

- local-variable inspection
- expression evaluation in paused debug contexts

### `UsageFindingCommandTools`

File: [src/Application/UsageFindingCommandTools.cs](../src/Application/UsageFindingCommandTools.cs)

Responsibilities:

- field read and write analysis
- type usage tracing

### `CodeAnalysisHelpers`

File: [src/Application/CodeAnalysisHelpers.cs](../src/Application/CodeAnalysisHelpers.cs)

Responsibilities:

- call graph analysis
- dependency-chain analysis
- cross-assembly dependency analysis
- dead-code approximation

### `De4dotTools`

Files:

- [src/Application/De4dotTools.cs](../src/Application/De4dotTools.cs)

Responsibilities:

- integrated deobfuscation flows
- obfuscator detection
- save/export of deobfuscated outputs
- in-process de4dot execution backed by assemblies built from the `de4dotEx/` submodule

### `ScriptTools`

File: [src/Application/ScriptTools.cs](../src/Application/ScriptTools.cs)

Responsibilities:

- `dnspy_execute_code`
- code-mode guidance and examples
- snippet validation
- optional advanced `dnspy_run_script`

### `WindowTools`

File: [src/Application/WindowTools.cs](../src/Application/WindowTools.cs)

Responsibilities:

- enumerate dnSpy-hosted dialogs
- dismiss dialogs that block reverse-engineering or debugger workflows

## Host integration and configuration UI

The extension also exposes dnSpy-side UI for controlling and inspecting the MCP server.

Relevant files:

- [src/Presentation/TheExtension.cs](../src/Presentation/TheExtension.cs)
- [src/Presentation/McpMenuCommands.cs](../src/Presentation/McpMenuCommands.cs)
- [src/Presentation/McpSettingsPage.cs](../src/Presentation/McpSettingsPage.cs)
- [src/Presentation/McpSettingsControl.xaml](../src/Presentation/McpSettingsControl.xaml)
- [src/Presentation/McpToolWindowViewModel.cs](../src/Presentation/McpToolWindowViewModel.cs)

Current host-side behavior:

- adds an `MCP` top-level menu in dnSpy
- keeps start, stop, restart, and "open config file" commands in that menu
- exposes MCP configuration under `Options -> MCP Server`
- shows live server status and live logs in the options UI
- keeps the shared view model in sync with the running server state

## Configuration and logging

Relevant files:

- [src/Configuration/McpConfig.cs](../src/Configuration/McpConfig.cs)
- [src/Presentation/McpSettings.cs](../src/Presentation/McpSettings.cs)
- [src/Helper/McpOutput.cs](../src/Helper/McpOutput.cs)

Configuration model:

- persisted in `mcp-config.json`
- surfaced in dnSpy through `Options -> MCP Server`
- includes host, port, API key, logging controls, and discovery-related settings such as `ExposeFullToolCatalog` and implicit default session behavior

Logging model:

- file logging
- dnSpy output-pane logging
- adjustable log level
- tool-call logging that can be enabled or disabled separately

## Public workflow groups

The public catalog is organized into workflow groups defined in [src/Application/ToolCatalog.cs](../src/Application/ToolCatalog.cs):

- `bootstrap`
- `reconstruction_core`
- `source_and_decompile`
- `architecture_and_xrefs`
- `metadata_and_native`
- `deobfuscation_and_recovery`
- `provenance_and_correlation`
- `editing_and_patchback`
- `debug_runtime`
- `memory_and_dumping`
- `ui_navigation`
- `scripting_advanced`
- `legacy_compat`

These groups are exposed through:

- `dnspy_list_tool_groups`
- `dnspy_enable_tool_groups`
- `dnspy_disable_tool_groups`
- `dnspy_get_enabled_tool_groups`

## Design intent

This repository is no longer trying to be just a flat collection of dnSpy wrappers. The v2 architecture is deliberately shaped around reconstruction workflows:

- first discover the relevant workflow area
- then enable or inspect only the tools needed for that workflow
- prefer code mode for composed investigations
- use direct dnSpy host-navigation tools when UI context is needed
- keep destructive or broad-surface capabilities explicit rather than ambient

## Related docs

- Main setup and client configuration: [README.md](../README.md)
- Exhaustive tool catalog: [docs/tool-reference.md](./tool-reference.md)
