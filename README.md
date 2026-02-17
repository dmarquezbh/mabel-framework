# Mabel Framework

> [Leia em Portugues / Read in Portuguese](README.pt-BR.md)

**Mabel** is a cross-platform mobile/desktop framework that uses **.NET/Blazor** as the UI language, compiled to **WASM/WASI**, and rendered via **native canvas** — no WebView.

## Why Mabel?

Existing cross-platform frameworks force you into WebViews (Ionic, Capacitor), proprietary languages (Flutter/Dart), or JavaScript (React Native). Mabel takes a different approach:

- **Write UI in Blazor** (`.razor` files) — C#, not JavaScript
- **Compile to WASM/WASI** — portable, sandboxed, no browser runtime
- **Render on native canvas** — Core Graphics (iOS), SkiaSharp (Desktop), Canvas (Android)
- **No WebView** — real native rendering performance
- **Hot reload** — Expo-style dev server pushes changes to your phone in real-time

## Architecture

```
  Blazor Components (.razor)
        |
        v
  Compiled to WASM/WASI
        |
        v
  Native Host loads WASM module
        |
        v
  Render Commands (WASI Protocol)
        |
        v
  Native Canvas (Core Graphics / SkiaSharp)
```

1. **Blazor components** (`.razor` files) are compiled to WASM/WASI — no browser, no WebView
2. A **native host** (Swift on iOS, Kotlin on Android, .NET on Desktop) loads the WASM module
3. Rendering via **native canvas** (Core Graphics on iOS, SkiaSharp on Desktop)
4. A **WASI protocol** bridges guest (WASM) and host (native) with render commands
5. The **`mabel` CLI** is built with .NET 10 AOT

## Project Structure

```
mabel-framework/
  Mabel.sln                          # Solution (6 projects)
  setup.sh                           # Dependency installer
  src/
    Mabel.Cli/                       # CLI entrypoint (thin, AOT-enabled)
      Program.cs                     # Arg parsing, delegates to features
    Mabel.Core/                      # Features, ports, infrastructure
      Domain/
        Platform.cs                  # Platform enum (Ios, Android, Desktop, All)
        ToolRequirement.cs           # KnownTools registry
      Ports/
        IShellExecutor.cs            # Shell abstraction
        IFileSystem.cs               # File system abstraction
      Infrastructure/
        BashShellExecutor.cs         # Real shell implementation
        LocalFileSystem.cs           # Real file system implementation
      Features/
        Doctor/DiagnoseEnvironment.cs
        Setup/RunSetup.cs
        Scaffold/CreateProject.cs
        Deploy/DeployToDevice.cs
        Devices/ListDevices.cs
        DevServer/MabelDevServer.cs  # HTTP + WebSocket hot reload server
        DevServer/RunDevServer.cs
        UsbHelp/UsbGuide.cs
    Mabel.Wasi.Protocol/             # WASI render protocol
      Protocol.cs                    # RenderOp, RenderCommand, InputEvent
      WasiContract.cs                # Guest/Host function names
    Mabel.Renderer/                  # Platform-agnostic renderer
      ICanvas.cs                     # Canvas abstraction
      MabelRenderer.cs               # Interprets RenderCommands -> ICanvas
    Mabel.Host.Ios/                  # iOS native host (Swift Package)
      Sources/MabelHost/
        MabelCanvasView.swift        # Core Graphics renderer
        MabelView.swift              # SwiftUI wrapper
        MabelEngine.swift            # WASM runtime integration
  tests/
    Mabel.Core.Tests/                # 5 tests (DiagnoseEnvironment)
    Mabel.Renderer.Tests/            # 16 tests (MabelRenderer)
  samples/                           # Sample projects (coming soon)
```

### Architecture: Vertical Slice + Hexagonal

- **Vertical Slice**: Each feature is self-contained in its own folder under `Features/`
- **Hexagonal/Ports-Adapters**: All external dependencies behind interfaces (`IShellExecutor`, `IFileSystem`). Real adapters in `Infrastructure/`, fakes in test projects
- Only 2 .NET projects for app code: `Mabel.Core` (features + ports + infra) and `Mabel.Cli` (thin entrypoint)

## CLI Commands

```bash
mabel doctor            # Check environment (tools, PATH, WSL detection)
mabel setup             # Install dependencies (.NET 10, Swift, xtool, wasmtime)
mabel setup --uninstall # Remove installed dependencies
mabel create <name>     # Scaffold a new Mabel project
mabel deploy [path]     # Build and run on a device/emulator
mabel dev [path]        # Start dev server with hot reload (Expo-style)
mabel devices           # List connected devices
mabel usb-help          # USB setup guide for physical devices
mabel version           # Show version
```

Options:
- `--platform, -p` — Target platform: `ios`, `android`, `desktop`, `all`
- `--bundle-id, -b` — Bundle ID for create (default: `com.example.<name>`)
- `--port, -P` — Dev server port (default: 5555)
- `--verbose` — Verbose output for dev server

## WASI Render Protocol

The guest (WASM) sends render commands to the host (native) as flat structs:

| Op          | Fields Used                                                |
|-------------|-----------------------------------------------------------|
| BeginFrame  | Color (background)                                         |
| Rect        | X, Y, W, H, Color                                         |
| RoundRect   | X, Y, W, H, Radius, Color                                 |
| Circle      | X (cx), Y (cy), Radius, Color                             |
| Line        | X (x1), Y (y1), W (x2), H (y2), Color                    |
| Text        | X, Y, Text, FontSize, Color                               |
| Image       | X, Y, W, H, Text (image ID)                               |
| PushClip    | X, Y, W, H                                                |
| PopClip     | (none)                                                     |
| PushOpacity | X (alpha 0-1)                                              |
| PopOpacity  | (none)                                                     |
| Translate   | X (dx), Y (dy)                                             |
| EndFrame    | (none)                                                     |

Color format: **RGBA** packed into `uint32` — `0xRRGGBBAA`

## Dev Server (Hot Reload)

`mabel dev` starts an Expo-style hot reload server:

1. Watches `.razor`, `.cs`, `.css`, `.html` files for changes
2. Recompiles WASM on file change (debounced 500ms)
3. Notifies connected clients via WebSocket
4. Serves the compiled WASM module over HTTP

Endpoints:
- `GET /mabel.wasm` — Compiled WASM module
- `GET /status` — JSON with build version, timestamp, client count
- `WebSocket /ws` — Sends `reload:<version>` on rebuild

## Getting Started

### Prerequisites

- Linux or WSL2 (Ubuntu recommended)
- Git

### 1. Clone and setup

```bash
git clone https://github.com/dmarquezbh/mabel-framework.git
cd mabel-framework
chmod +x setup.sh
./setup.sh
```

This installs:
- .NET 10 SDK
- Swift toolchain (for iOS)
- xtool (iOS deployment from Linux)
- wasmtime (WASM runtime)
- usbmuxd + libimobiledevice (iOS USB)
- adb (Android USB)

### 2. Verify environment

```bash
# Ensure dotnet is on PATH
export PATH="$HOME/.dotnet:$PATH"

# Check everything is installed
dotnet run --project src/Mabel.Cli -- doctor
```

### 3. Build and test

```bash
dotnet build
dotnet test
```

## iOS Development from Linux (WSL2)

Mabel supports deploying to physical iPhones from WSL2:

1. Connect iPhone via USB
2. Pass USB device to WSL: `usbipd attach --wsl --busid <busid>`
3. Restart usbmuxd: `sudo systemctl restart usbmuxd`
4. Verify: `idevice_id -l` should show your device UDID
5. Run `mabel usb-help` for detailed step-by-step instructions

## Testing

```bash
dotnet test                    # Run all 21 tests
dotnet test --filter Renderer  # Run only renderer tests
dotnet test --filter Core      # Run only core tests
```

Test infrastructure uses fakes (not mocks):
- `FakeShellExecutor` — Records commands, returns configured results
- `FakeFileSystem` — In-memory file system
- `FakeCanvas` — Records draw calls for assertion

## Technology Stack

- **.NET 10** — SDK, CLI (AOT), Blazor components
- **WASM/WASI** — Portable binary format, no browser needed
- **Swift** — iOS native host (Core Graphics rendering)
- **SkiaSharp** — Desktop rendering (planned)
- **xunit v3** — Testing framework
- **xtool** — iOS deployment from Linux

## Roadmap

- `mabel ai` — LLM-powered prompt-to-UI generation
- WASI Component Model — Universal package system (packages from any language)
- Android host (Kotlin + Canvas)
- Desktop host (SkiaSharp)
- Sample hello world project

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## License

MIT
