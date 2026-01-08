# 🦋 Mabel Framework - AI Development Guide

This document provides essential context and instructions for AI agents (Copilot, Cursor, Antigravity, etc.) to assist in developing and maintaining projects built with the Mabel Framework.

## 🚀 Overview
**Mabel** is a lightweight, cross-platform framework designed to wrap Web UIs (like Blazor WASM, React, or Vue) into high-performance native applications for **iOS, Android, and Desktop**—all controllable from a **Linux** environment.

## 🛠️ Key Tools
- **xtool**: The core tool for iOS development on Linux (build, sign, deploy).
- **dotnet**: Used for Blazor WebAssembly UI development.
- **mabel.sh**: The CLI for scaffolding and system management.

## 📁 Project Structure
A standard Mabel project follows this layout:
- `blazor_app/`: The source code for the Web UI (C# / Blazor WASM).
- `ios_app/`: The native Swift wrapper (initialized via `xtool`).
  - `Sources/ios_app/ContentView.swift`: Main entry point and Native Bridge configuration.
  - `Sources/ios_app/Resources/`: Contains the synchronized assets from the Web UI.
- `mabel-sync.sh`: Automation script to publish the web app and update native assets.

## 🔄 Development Lifecycle
1. **Modify UI**: Edit components in `blazor_app/`.
2. **Synchronize**: Run `./mabel-sync.sh` in the project root. This publishes the Blazor app and updates the `ios_app/Resources` directory.
3. **Deploy**: Navigate to `ios_app/` and run `xtool dev` to deploy to a physical device.

## 🌉 Native Bridge (JS ↔ Swift)
### Sending from Web to Native
In JavaScript/C#, use the injected `window.callNative` function:
```javascript
window.callNative("Hello from Mabel UI!");
```

### Handling in Swift
The `MabelBridge` class in `ContentView.swift` handles incoming messages:
```swift
class MabelBridge: NSObject, WKScriptMessageHandler {
    func userContentController(_ uc: WKUserContentController, didReceive msg: WKScriptMessage) {
        if msg.name == "iosNative", let body = msg.body as? String {
            // Process message
            NSLog("🦋 [MABEL-BRIDGE] \(body)")
        }
    }
}
```

## 📝 Important Notes for AI
- **HMR**: For Hot Module Replacement, set `devMode = true` in `ContentView.swift` to point to the local dev server IP.
- **Asset Loading**: Assets are served via the `app://` scheme to bypass CORS and improve performance.
- **Deployment**: Always remind the user that `xtool` requires a physical device and proper Apple ID authentication (`mabel` option 1).
