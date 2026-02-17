# Mabel Framework - AI Development Guide

This document provides context for AI agents assisting with Mabel Framework development.

## Architecture

Mabel is a cross-platform framework: Blazor components compiled to WASM/WASI, rendered via native canvas. **No WebView.**

```
Blazor (.razor) -> WASM/WASI -> Native Host -> Canvas Rendering
```

### Key Principles
- **Vertical Slice Architecture** — each feature is self-contained under `Features/`
- **Hexagonal/Ports-Adapters** — all I/O behind interfaces (`IShellExecutor`, `IFileSystem`)
- **Only 2 app projects** — `Mabel.Core` (everything) and `Mabel.Cli` (thin entrypoint)
- **.NET 10** everywhere, `net10.0` TFM
- **xunit v3** (3.2.2) for tests — NOT xunit v2

### Solution Structure (6 projects)
```
Mabel.sln
  src/Mabel.Wasi.Protocol/   # RenderOp, RenderCommand, InputEvent, WasiContract
  src/Mabel.Renderer/        # ICanvas, MabelRenderer
  src/Mabel.Core/             # Domain, Ports, Infrastructure, Features
  src/Mabel.Cli/              # CLI entrypoint (AOT-enabled)
  tests/Mabel.Core.Tests/
  tests/Mabel.Renderer.Tests/
```

Plus `src/Mabel.Host.Ios/` (Swift Package, not in .sln).

## Development Environment

- **.NET 10 SDK** — requires `export PATH="$HOME/.dotnet:$PATH"`
- Developed and tested on **Linux / WSL2**

## Build & Test

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build              # Must succeed with 0 errors, 0 warnings
dotnet test               # Must pass all 21 tests (16 renderer + 5 core)
```

## Code Conventions

### Namespaces
- `Mabel.Wasi.Protocol` — protocol types
- `Mabel.Renderer` — ICanvas, MabelRenderer
- `Mabel.Core.Domain` — Platform, ToolRequirement
- `Mabel.Core.Ports` — IShellExecutor, IFileSystem
- `Mabel.Core.Infrastructure` — BashShellExecutor, LocalFileSystem
- `Mabel.Core.Features.<Name>` — each feature in its own namespace

### Testing
- Fakes, not mocks (`FakeShellExecutor`, `FakeFileSystem`, `FakeCanvas`)
- Tests use `using Xunit;` explicitly (ImplicitUsings does not include it)
- xunit v3 `[Fact]` attribute

### Security
- `BashShellExecutor` uses single-quote escaping for shell args
- `MabelDevServer` uses `System.Text.Json` for JSON serialization (no string interpolation)
- Thread safety via `Interlocked` and `lock` in `MabelDevServer`

## WASI Protocol

Color format: RGBA packed as `uint32` — `0xRRGGBBAA`.

`RenderCommand.Text` is `string?`. For binary WASI transport, this needs separate serialization (pointer + length). Current design is for in-process use.

The `ICanvas` interface includes `SaveState()`/`RestoreState()` to prevent transform leaks across frames. `BeginFrame` saves state, `EndFrame` restores it.

## CLI

The CLI at `src/Mabel.Cli/Program.cs` uses top-level statements. Commands: `doctor`, `setup`, `create`, `deploy`, `dev`, `devices`, `usb-help`, `version`, `help`.

`GetPositional()` skips flags and their values when finding positional arguments.

All `PlatformExtensions.Parse()` calls are wrapped in try/catch.

## Files Changed in Code Review

These files had fixes applied from a comprehensive 3-agent code review:

1. `BashShellExecutor.cs` — command injection fix, deadlock fix, null check
2. `Platform.cs` — RemoveEmptyEntries, composite Label
3. `ToolRequirement.cs` — added adb for Android
4. `DiagnoseEnvironment.cs` — home parameter, try/catch WSL check
5. `MabelDevServer.cs` — thread safety (Interlocked), resource disposal, JSON injection, graceful WebSocket close, concurrent rebuild prevention
6. `MabelRenderer.cs` — SaveState/RestoreState on BeginFrame/EndFrame, Image op support, default case
7. `ICanvas.cs` — added DrawImage, SaveState, RestoreState, color format docs
8. `Protocol.cs` — comprehensive field documentation, color format docs, string field note
9. `Program.cs` — GetPositional skips flags, PlatformExtensions.Parse error handling
10. `MabelCanvasView.swift` — saveGState/restoreGState around BeginFrame/EndFrame, translate fix
