# 🦋 Mabel Framework

**Mabel** is a lightweight, cross-platform framework designed to wrap Web UIs (like Blazor WASM, React, or Vue) into high-performance native applications for **iOS, Android, and Desktop**—all controllable from a **Linux** environment.

## 🚀 Vision
Built for developers who want the beauty of the web with the power of native APIs, without needing a Mac.

## ✨ Features
- **Linux-First iOS Development**: Build, sign, and deploy to physical iPhones from Linux using the `xtool` toolchain.
- **Native Bridge**: Direct communication between JavaScript/C# and Swift/Kotlin/C#.
- **HMR Support**: Live reload your Blazor app directly on an iPhone via Wi-Fi.
- **Micro-Footprint**: No heavy Electron. Uses native WebViews (WebKit/WebKitGTK).

## 🛠️ Getting Started

### 1. Installation
The `mabel` CLI is your main tool.
```bash
# Assuming you have the mabel script
sudo cp mabel.sh /usr/local/bin/mabel
sudo chmod +x /usr/local/bin/mabel
```

### 2. Create your first project
```bash
mabel
# Choose option 4: New Project (Blazor WASM + iOS)
```

### 3. Build & Deploy
Inside your project folder:
```bash
./mabel-sync.sh   # Compiles and prepares assets
cd ios_app
xtool dev         # Deploys to your iPhone
```

## 🗺️ Roadmap
- [x] iOS support via Linux (`xtool`)
- [x] Blazor WASM Native Bridge
- [ ] Desktop support via **Photino** (.NET)
- [ ] Android support via Linux SDK
- [ ] Unified Plugin System

## 📜 License
MIT
