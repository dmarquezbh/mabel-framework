import AppKit
import MabelHost

// =============================================================================
// Mabel Host macOS — demo app entry point.
//
// Real AppKit application: builds a NSWindow, drops a MabelCanvasView in it,
// and renders the static "hello world" Mabel display-list via Core Graphics.
//
// BUILD (from Linux/WSL, no Mac):
//   swift build --swift-sdk arm64-apple-macosx --package-path src/Mabel.Host.MacOS
//
// The produced Mach-O executable is bundled into MabelHost.app and ad-hoc
// signed with rcodesign (see docs/macos-host.md). RUNNING it still requires a
// Mac (or macOS VM) — that step is deferred, documented as a known limitation.
// =============================================================================

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    let engine = MabelEngine()

    func applicationDidFinishLaunching(_ notification: Notification) {
        let frame = NSRect(x: 0, y: 0, width: 390, height: 640)

        window = NSWindow(
            contentRect: frame,
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Mabel Host — macOS"
        window.center()

        let canvas = MabelCanvasView(frame: frame)
        canvas.autoresizingMask = [.width, .height]
        window.contentView = canvas

        engine.onChange = { [weak canvas, weak self] in
            guard let self else { return }
            canvas?.commands = self.engine.commands
        }
        engine.loadHelloWorld()

        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
