# Third-Party Notices

This repository is distributed under the GNU General Public License v3.0. Third-party code, binaries, and source-derived adaptations used in this repository must be tracked here.

## Policy

- Keep provenance for every imported or adapted third-party source file or code fragment.
- Prefer local adaptation over direct runtime dependencies when integrating external MCP server code.
- Update this document when vendoring, adapting, or replacing third-party components.

## Current Third-Party Components

### dnSpy / dnSpyEx

- License: GPLv3
- Upstream: [dnSpyEx/dnSpy](https://github.com/dnSpyEx/dnSpy) — the most actively maintained fork of the original [dnSpy/dnSpy](https://github.com/dnSpy/dnSpy)
- Usage: host application contracts and extension integration
- Location: `dnSpy/` submodule and project references
- Notes: required host/runtime integration

### de4dot / de4dotEx

- License: GPLv3
- Upstream: [GDATAAdvancedAnalytics/de4dotEx](https://github.com/GDATAAdvancedAnalytics/de4dotEx) - the most actively maintained fork of the original [de4dot/de4dot](https://github.com/de4dot/de4dot) as of March 2026, with merged contributions from several other forks (ConfuserEx, DoubleZero, VirtualGuard support)
- Usage: deobfuscation engine libraries
- Location: `vendors/de4dotEx/` submodule
- Integration mode: submodule source checkout, built locally into `artifacts/de4dotEx/net8/`
- Notes: the MCP server references the generated library payload; no external `de4dot.exe` runner is shipped

### dnSpy.Extension.HoLLy

- License: GPLv3
- Upstream: [geocine/dnSpy.Extension.HoLLy](https://github.com/geocine/dnSpy.Extension.HoLLy)
- Usage: separate dnSpy extension built and installed alongside the MCP server for local testing
- Location: `extensions/dnSpy.Extension.HoLLy/` submodule
- Integration mode: separate extension payload copied into dnSpy's `Extensions/dnSpy.Extension.HoLLy/` output folder
- Notes: remains independently buildable and keeps its own upstream license and changelog

### Echo

- License: LGPLv3
- Upstream: [Washi1337/Echo](https://github.com/Washi1337/Echo)
- Usage: control-flow and data-flow support used by the HoLLy extension
- Location: `vendors/Echo/` submodule
- Integration mode: centralized vendor checkout consumed by HoLLy during monorepo builds
