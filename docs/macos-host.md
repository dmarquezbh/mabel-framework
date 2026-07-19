# Mabel Host — macOS (AppKit), built without a Mac

`Mabel.Host.MacOS` is the AppKit host for the Mabel SDUI render protocol. It
renders a Mabel display-list (the `RenderCommand` list defined by
`Mabel.Wasi.Protocol`) into an `NSView` via **Core Graphics** — no WebView, no
SwiftUI. It is the desktop twin of `Mabel.Host.Ios` (UIKit) and shares the exact
same binary `RenderOp` contract, so both hosts render pixel-identical frames.

The point of this spike: **build and sign a real macOS `.app` entirely on
Linux/WSL, with no Mac in the loop.**

## Status

| Step | State | Notes |
|------|-------|-------|
| Cross-compile AppKit host on Linux | ✅ DONE | `swift build --swift-sdk arm64-apple-macosx`, links against `AppKit`/`Foundation`/`CoreGraphics` from the macOS SDK. |
| Package `.app` bundle | ✅ DONE | `Info.plist` + Mach-O laid out as `MabelHost.app`. |
| Ad-hoc code signing on Linux | ✅ DONE | `rcodesign sign` embeds a real Mach-O signature (CodeDirectory + RequirementSet + ad-hoc CMS). |
| **Run / launch the app** | ⛔ BLOCKED — needs a Mac | AppKit only runs on macOS. There is no Linux runtime. Deferred by design. |
| Distribution signing (Developer ID / notarization) | ⛔ BLOCKED — needs Apple cert | Ad-hoc only for now; notarization requires an Apple Developer identity + Apple's notary service. |

## What makes the no-Mac build possible

- **Swift 6.1 toolchain** on Linux (`$HOME/swift`, → `swift-6.1-RELEASE-ubuntu24.04`).
- **Darwin Swift SDK** (from xtool) registered as `darwin`
  (`swift sdk list`). Its `swift-sdk.json` declares macOS target triples
  `arm64-apple-macosx` / `x86_64-apple-macosx` with `sdkRootPath` pointing at
  `MacOSX15.5.sdk`. That SDK ships the `AppKit`, `Cocoa`, `Foundation`, and
  `CoreGraphics` frameworks we link against.
- **rcodesign** (`apple-codesign` crate) — a pure-Rust reimplementation of
  Apple's `codesign` that runs on Linux. Ad-hoc signing needs no Apple
  certificate.

## Build it

```bash
# from the repo root, on Linux/WSL
bash src/Mabel.Host.MacOS/build-macos.sh
```

The script:

1. Cross-compiles with `swift build --swift-sdk arm64-apple-macosx`.
2. Assembles `src/Mabel.Host.MacOS/build/MabelHost.app` (Info.plist + Mach-O).
3. Ad-hoc signs the bundle with `rcodesign sign`.

### The one Linux gotcha: SwiftPM's `codesign` step

SwiftPM automatically applies debug entitlements to executable products by
invoking Apple's `codesign`, which does not exist on Linux — so a bare
`swift build --swift-sdk arm64-apple-macosx` ends with:

```
error: command Applying debug entitlements to .../MabelHostApp failed:
unable to spawn process 'codesign' (No such file or directory)
```

The Mach-O is **fully linked before** that step, so the binary is already
valid. `build-macos.sh` puts a no-op `codesign` shim on `PATH` for the build
(so SwiftPM's step is a clean exit) and then does the **real** signing with
`rcodesign` afterwards.

## Verified output (Linux/WSL, this spike)

Toolchain: `Swift 6.1 (swift-6.1-RELEASE)`, `apple-codesign 0.29.0`, `xtool 1.17.0`, macOS SDK `MacOSX15.5.sdk`.

```
==> Building (swift build --swift-sdk arm64-apple-macosx)
[11/13] Linking MabelHostApp
[12/13] Applying MabelHostApp
Build complete! (16.19s)
==> Linked Mach-O: Mach-O 64-bit arm64 executable, flags:<NOUNDEFS|DYLDLINK|TWOLEVEL|PIE>
==> Packaging MabelHost.app
==> Signing with rcodesign (ad-hoc)
signing main executable Contents/MacOS/MabelHost
```

`rcodesign print-signature-info` on the signed Mach-O confirms a real embedded
signature (ad-hoc → empty CMS blob, which is expected):

```
signature:
  superblob_length: 2661
  blob_count: 3
  blobs:
  - slot: CodeDirectory (0)      magic: fade0c02  length: 2605
  - slot: RequirementSet (2)     magic: fade0c01  length: 12
  - slot: CMS Signature (65536)  magic: fade0b01  length: 8
```

## Why RUN is deferred

AppKit is macOS-only; there is no way to execute the `.app` on Linux. To see it
render you must copy `MabelHost.app` to a Mac (or a macOS VM) and launch it.
Because it is ad-hoc signed (not Developer-ID signed or notarized), Gatekeeper
will require a right-click → Open, or clearing the quarantine attribute
(`xattr -dr com.apple.quarantine MabelHost.app`), on first launch.

That last mile is the only part of the macOS host that genuinely needs Apple
hardware. Everything up to a signed bundle is reproducible on Linux.

## Layout

```
src/Mabel.Host.MacOS/
  Package.swift                       # MabelHost (lib) + MabelHostApp (exe)
  build-macos.sh                      # no-Mac build + package + sign pipeline
  Sources/
    MabelHost/
      RenderProtocol.swift            # RenderOp/RenderCommand/HitRegion (mirrors Mabel.Wasi.Protocol)
      MabelCanvasView.swift           # NSView + Core Graphics renderer (AppKit twin of iOS host)
      MabelEngine.swift               # display-list owner + static demos
    MabelHostApp/
      main.swift                      # NSApplication demo window
```
