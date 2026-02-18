# Phase 2 Roadmap

> [Leia em Portugues](PHASE2.pt-BR.md)

This document specifies Phase 2 of the Mabel Framework — features planned after the core rendering pipeline is stable.

## Overview

Phase 2 focuses on three pillars:

1. **GitHub Pages** — Project website with landing page and documentation
2. **Browser Playground** — Code and preview Mabel apps directly in the browser
3. **AI Assistant** — LLM-powered help for building UIs, available via cloud or local inference
4. **Telegram Bot** — Chat with the AI assistant from Telegram
5. **Native API Access** — WASI Capability Providers for device APIs (camera, GPS, etc.)

---

## 1. GitHub Pages — Project Website

### Stack

**Blazor WebAssembly** (dogfooding — the site itself is built with the same technology as the framework).

### Pages

- **Landing page** — Fancy, animated, explains what Mabel is and why it matters
- **Getting Started** — Interactive tutorial
- **API Reference** — Generated from XML docs
- **Playground** — See section 2 below
- **Blog** — Release notes, tutorials, architecture decisions

### Hosting

GitHub Pages (static files). Blazor WASM compiles to static HTML/JS/WASM — no server needed.

---

## 2. Browser Playground

### Goal

A user can write a Mabel app **entirely in the browser** and see it render in real-time. No server, no installation, no backend — everything runs client-side via WASM.

### Architecture

```
Browser
  |
  +-- Monaco Editor (JS) -- code editing with syntax highlighting
  |
  +-- Roslyn (via .NET WASM) -- compiles C# to IL
  |
  +-- Mabel Renderer (WASM) -- interprets RenderCommands
  |
  +-- Canvas Preview (HTML5 Canvas / SVG) -- visual output
```

### Technical Approach

1. **Monaco Editor** — Embedded via JS interop in Blazor. Provides C# syntax highlighting. Full IntelliSense is deferred (requires loading ~50-100MB of Roslyn language services).

2. **Compilation** — Two-tier approach:
   - **Quick mode**: Pre-compiled templates. User modifies parameters (colors, text, layout) and sees changes instantly. No Roslyn needed.
   - **Full mode**: Roslyn compiler loaded in-browser via .NET WASM. User writes arbitrary C# that produces `RenderCommand[]`. Compilation takes 2-5 seconds. Initial download: ~50-100MB of compiler assemblies (cached after first load).

3. **Rendering** — A JavaScript/Canvas2D implementation of `ICanvas` that renders `RenderCommand[]` directly in the browser. This is a new canvas backend (like `MabelCanvasView.swift` is for iOS).

4. **No server**: Everything runs client-side. The compiled IL executes in the .NET WASM runtime already loaded by Blazor.

### Realistic Constraints

- **Download size**: First visit requires downloading the .NET WASM runtime (~15MB) + Roslyn compiler (~50-100MB for full mode). Progressive loading with quick mode available immediately.
- **Compile time**: 2-5 seconds for simple programs in full mode. Quick mode is instant.
- **Memory**: Browser WASM has a 4GB memory limit. This is sufficient for the playground.
- **Razor compilation**: Not feasible client-side (requires the full Razor SDK + Roslyn + all reference assemblies = 100-200MB+). The playground compiles plain C# that produces RenderCommands, not `.razor` files.

---

## 3. AI Assistant

### Goal

Help users build Mabel UIs by describing what they want in natural language. The AI generates `RenderCommand[]` code or full component layouts.

### Two Modes

#### Cloud Mode (Recommended)

User provides their own API key for:
- **GitHub Copilot** (via GitHub token)
- **Anthropic Claude** (API key)
- **OpenAI** (API key)
- **Any OpenAI-compatible API** (custom endpoint + key)

Keys are stored **only in the browser** (localStorage). Never sent to any Mabel server — the browser calls the LLM API directly (CORS permitting) or through a minimal proxy that does not log requests.

#### Local Mode (Experimental)

A small LLM running **entirely in the browser** via WebGPU.

**Approach**: Use [web-llm](https://github.com/mlc-ai/web-llm) (MLC AI) — the most mature browser LLM runtime. It uses WebGPU for GPU-accelerated inference.

**Viable models**:
- Qwen2-0.5B (~50+ tok/s on modern GPU)
- Phi-3-mini 3.8B (~20-40 tok/s on discrete GPU)
- TinyLlama 1.1B (~30-40 tok/s)

**Requirements**: Browser with WebGPU support (Chrome 113+, Edge 113+). Falls back to cloud mode if WebGPU is not available.

**Realistic assessment**: Local mode with small models (0.5B-1B) can handle simple tasks like "make the background blue" or "add a button at the bottom". Complex multi-component layouts require larger models (3B+) or cloud mode. This is experimental — quality will improve as models get better.

### 1-Bit LLMs (Future Research)

BitNet b1.58 (ternary weights: {-1, 0, +1}) offers dramatically smaller model sizes:
- 0.7B model = ~125MB (vs ~600MB for standard Q4)
- 2.4B model = ~400MB (vs ~1.5GB for standard Q4)

**Current status**: No browser runtime exists for 1-bit inference. [BitNet.cpp](https://github.com/microsoft/BitNet) (Microsoft) is CPU-only native code. WebGPU kernels for ternary weight dequantization would need to be created from scratch.

**Path forward**: If/when web-llm or another project adds BitNet b1.58 support, this would enable larger models in the browser at lower memory cost. We monitor this space and will integrate when viable.

---

## 4. Telegram Bot

### Goal

Developers can chat with the Mabel AI from Telegram — ask questions, generate UI code, get help with the framework.

### Architecture

```
Telegram Bot API
      |
      v
Bot Server (lightweight .NET service or serverless function)
      |
      +-- User sends message: "Create a login screen"
      |
      v
LLM Provider (user's configured provider)
      |
      v
Bot responds with generated RenderCommand[] code
```

### Implementation

- **Telegram Bot API** — Standard bot using `Telegram.Bot` NuGet package or raw HTTP
- **LLM routing** — User configures their LLM provider via bot commands (`/config provider anthropic`, `/config key sk-...`)
- **Keys stored server-side** — Encrypted, per-user. Or user can use the `/ask` command with inline key (key not stored)
- **Code output** — Bot responds with formatted C# code blocks that the user can paste into their Mabel project
- **Can also run in serverless** — Azure Functions, AWS Lambda, or any container

### Realistic Assessment

This is the simplest feature in Phase 2. A Telegram bot that proxies to an LLM API is straightforward. The main work is prompt engineering to generate good Mabel-specific code.

---

## 5. WASI Capability Providers — Native API Access

### Goal

Allow Mabel apps to access device APIs (camera, GPS, notifications, sensors, etc.) through a clean, cross-platform interface — without the developer creating native packages manually.

### Architecture

```
Guest (WASM)                              Host (Native)
  |                                          |
  | wasi_capability_request("camera.capture", params)
  |----------------------------------------->|
  |                                          |
  |                    Swift: AVFoundation   |
  |                    Kotlin: Camera2 API   |
  |                    Desktop: OS API       |
  |                                          |
  |<-----------------------------------------|
  | wasi_capability_response(result)         |
```

### How It Works

1. **Guest side**: The WASM module calls `wasi_capability_request(capability, params)` — a WASI function export
2. **Host side**: The native host resolves the capability name to a native implementation
3. **Swift (iOS)**: Uses existing Swift Package Manager (SPM) packages from the community. The host provides a thin binding layer that delegates to SPM packages
4. **Kotlin (Android)**: Same pattern, using Gradle/Maven packages
5. **Desktop (.NET)**: Direct .NET API calls

### Capability Registry

```json
{
  "capabilities": {
    "camera.capture": {
      "ios": "AVFoundation",
      "android": "Camera2",
      "desktop": "System.Drawing"
    },
    "location.current": {
      "ios": "CoreLocation",
      "android": "FusedLocationProvider",
      "desktop": "GeoCoordinateWatcher"
    },
    "notification.send": {
      "ios": "UserNotifications",
      "android": "NotificationManager",
      "desktop": "ToastNotification"
    }
  }
}
```

### Why SPM (Swift Package Manager)?

For iOS, instead of creating packages from scratch, we leverage the existing SPM ecosystem:

- Thousands of packages already available
- Apple's official SDK frameworks (AVFoundation, CoreLocation, etc.) need no extra packages
- The Mabel host is already a Swift Package — adding SPM dependencies is trivial
- Community packages for complex features (e.g., Firebase, Stripe) can be added as SPM deps

The Mabel host doesn't reinvent the wheel — it provides **bindings** that delegate to existing packages.

### Planned Capabilities (v1)

| Capability | iOS | Android | Desktop |
|-----------|-----|---------|---------|
| `camera.capture` | AVFoundation | Camera2 | - |
| `camera.picker` | UIImagePickerController | Intent.ACTION_PICK | FileDialog |
| `location.current` | CoreLocation | FusedLocationProvider | - |
| `notification.local` | UserNotifications | NotificationManager | ToastNotification |
| `haptics.impact` | UIImpactFeedbackGenerator | Vibrator | - |
| `share.text` | UIActivityViewController | Intent.ACTION_SEND | - |
| `clipboard.copy` | UIPasteboard | ClipboardManager | Clipboard |
| `storage.keyvalue` | UserDefaults | SharedPreferences | Preferences |
| `biometric.auth` | LocalAuthentication | BiometricPrompt | - |

---

## Timeline

| Feature | Complexity | Status |
|---------|-----------|--------|
| GitHub Pages (landing) | Medium | Planned |
| Playground (quick mode) | Medium | Planned |
| Playground (full Roslyn) | High | Planned |
| AI (cloud mode) | Low | Planned |
| AI (local/WebGPU) | High | Research |
| Telegram Bot | Low | Planned |
| WASI Capabilities (v1) | High | Planned |
| 1-bit LLM browser inference | Very High | Research |

---

## Technical Decisions

### Why Blazor WASM for the site?

Dogfooding. The project site built with the same technology demonstrates that Blazor WASM works for real applications. It also means contributors working on the site are learning the same stack used by the framework.

### Why not server-side compilation for the playground?

Mabel's philosophy is **no server dependency**. The playground should work offline, on an airplane, without internet. Server-side compilation is faster and easier, but goes against the project's values. We accept the tradeoff of larger initial download for full client-side independence.

### Why allow cloud LLM providers?

Local models (0.5-3B parameters) produce lower quality output than cloud models (70B+). For production-quality UI generation, cloud models are significantly better. The cloud option respects user privacy by using their own keys and never routing through Mabel servers.
