# Tutorial: Your First Mabel App

> [Leia em Portugues](TUTORIAL.pt-BR.md)

This tutorial walks you through building your first Mabel app — from zero to
a running render frame. By the end, you'll understand the core rendering pipeline
and be ready to build real UIs.

## What You'll Learn

1. How Mabel's rendering pipeline works
2. How to create render commands
3. How to use the `MabelRenderer` with a canvas
4. How to run the hello world sample
5. How `mabel live` hot reload works

## Prerequisites

- Linux or WSL2 (Ubuntu recommended)
- Git installed

## Step 1: Clone and Setup

```bash
git clone https://github.com/dmarquezbh/mabel-framework.git
cd mabel-framework
chmod +x setup.sh
./setup.sh
```

After setup completes, ensure .NET is on your PATH:

```bash
export PATH="$HOME/.dotnet:$PATH"
```

> **Tip**: Add this line to your `~/.bashrc` so you don't have to type it every time.

Verify everything is installed:

```bash
dotnet run --project src/Mabel.Cli -- doctor
```

You should see checkmarks next to all required tools.

## Step 2: Build the Framework

```bash
dotnet build
dotnet test
```

All 21 tests should pass. If they don't, run `mabel setup` to install missing
dependencies.

## Step 3: Run the Hello World

```bash
dotnet run --project samples/hello-world
```

You should see output like this:

```
  Mabel Framework - Hello World
  ─────────────────────────────

  Screen: 390x844 (simulated iPhone 14)

=== Mabel Render Frame ===
  Commands: 16

  [BeginFrame  ] background=#1A1A2EFF
  [Rect        ] x=0 y=0 w=390 h=80 color=#6C63FFFF
  [Text        ] x=115 y=30 size=24 color=#FFFFFFFF "Mabel Framework"
  [Text        ] x=95 y=160 size=36 color=#FFFFFFFF "Hello, Mabel!"
  ...
  [EndFrame    ]

=== End Frame ===
```

Each line is a **render command** — the same format that gets sent from your
WASM module to the native host via the WASI protocol.

## Step 4: Understanding the Render Pipeline

Mabel's architecture has 4 layers:

```
  Your App Code (Blazor/.razor)
       |
       | Compiles to
       v
  WASM/WASI Module
       |
       | Sends RenderCommands via WASI protocol
       v
  MabelRenderer (interprets commands)
       |
       | Calls ICanvas methods
       v
  Native Canvas (Core Graphics / SkiaSharp)
```

### RenderCommands

Every visual element is a `RenderCommand` — a flat struct with an `Op` (what to
draw) and parameters (position, size, color, text):

```csharp
using Mabel.Wasi.Protocol;

// Draw a purple rectangle
var rect = new RenderCommand(RenderOp.Rect, x: 10, y: 10, w: 200, h: 50, color: 0x6C63FFFF);

// Draw white text
var text = new RenderCommand(RenderOp.Text, x: 20, y: 25, 0, 0, color: 0xFFFFFFFF,
    Text: "Hello!", FontSize: 18);
```

### Color Format

Colors are RGBA packed into a `uint32`:

```
0xRRGGBBAA

Examples:
  0xFFFFFFFF = white (full opacity)
  0x000000FF = black (full opacity)
  0xFF000080 = red (50% opacity)
  0x6C63FFFF = purple (full opacity)
```

### Available Operations

| Op | What it draws |
|----|---------------|
| `BeginFrame` | Start a new frame, set background color |
| `Rect` | Filled rectangle |
| `RoundRect` | Rounded rectangle |
| `Circle` | Filled circle |
| `Line` | Line between two points |
| `Text` | Text string |
| `Image` | Image by ID |
| `PushClip` / `PopClip` | Clip drawing to a rectangle |
| `PushOpacity` / `PopOpacity` | Set opacity for subsequent draws |
| `Translate` | Move the coordinate origin |
| `EndFrame` | Finish the frame |

### MabelRenderer

The `MabelRenderer` takes a list of commands and draws them on an `ICanvas`:

```csharp
using Mabel.Renderer;
using Mabel.Wasi.Protocol;

// The canvas is provided by the platform:
//   iOS:     MabelCanvasView (Core Graphics)
//   Desktop: SkiaSharpCanvas (planned)
//   Tests:   FakeCanvas
ICanvas canvas = GetPlatformCanvas();

var renderer = new MabelRenderer(canvas);
renderer.Render(commands);
```

The renderer handles `SaveState`/`RestoreState` automatically — each frame
starts with a saved state and restores it at the end, so transforms (Translate)
don't leak between frames.

## Step 5: Modify the Hello World

Open `samples/hello-world/HelloApp.cs` and try changing things:

### Change the background color

```csharp
// Line in BuildFrame — change DarkBlue to any color
commands.Add(Frame(RenderOp.BeginFrame, 0x2D2D44FF)); // dark gray-blue
```

### Add a new element

```csharp
// Add after the footer text
commands.Add(RoundRect(40, 660, screenWidth - 80, 80, 0x4CAF50FF, 12)); // green card
commands.Add(Text(60, 685, "Built with .NET 10", 16, White));
```

### Run again to see changes

```bash
dotnet run --project samples/hello-world
```

## Step 6: How Mabel Live Works

When you're developing a real Mabel app (not just the sample), you use
`mabel live` for hot reload:

```bash
dotnet run --project src/Mabel.Cli -- live [project-path]
```

This starts the **Mabel Live** server:

1. **Builds** your Blazor project to WASM
2. **Watches** for file changes (`.razor`, `.cs`, `.css`, `.html`)
3. **Recompiles** automatically when you save (500ms debounce)
4. **Notifies** connected devices via WebSocket
5. The device **downloads** the new WASM and re-renders instantly

```
  [live] Building WASM...
  [live] Build OK
  [live] Watching for file changes...
  [live] Dev server running on http://192.168.1.100:5555
    WASM:      http://192.168.1.100:5555/mabel.wasm
    WebSocket: ws://192.168.1.100:5555/ws
    Status:    http://192.168.1.100:5555/status

  [live] File changed — rebuilding...
  [live] Build OK (v2)
  [live] Notified 1 client(s)
```

Options:
- `--port, -P` — Change the port (default: 5555)
- `--verbose` — Show detailed logs

## Step 7: Create a New Project

To scaffold a full Mabel project with native hosts:

```bash
dotnet run --project src/Mabel.Cli -- create my-app --platform ios
```

This generates:

```
my-app/
  mabel.json           # Project manifest
  web_app/             # Blazor WASM project (your UI code)
  ios_app/             # iOS native host (Swift Package)
    Package.swift
    xtool.yml
    Sources/ios_app/
      ContentView.swift
```

## What's Next

- **Deploy to iPhone**: Connect via USB and run `mabel deploy`
- **Add more UI elements**: Use all the `RenderOp` operations
- **Native APIs**: WASI Capability Providers for camera, GPS, etc. (coming soon)
- **Mabel Live**: Real-time hot reload during development

## Architecture Deep Dive

For more details on the architecture, see:
- [AGENT.md](../AGENT.md) — Development guide with conventions
- [PHASE2.md](PHASE2.md) — Phase 2 roadmap (playground, AI, GitHub Pages)
- Source code in `src/Mabel.Wasi.Protocol/Protocol.cs` — Full protocol documentation
