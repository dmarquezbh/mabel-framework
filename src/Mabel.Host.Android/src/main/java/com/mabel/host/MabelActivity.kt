package com.mabel.host

import android.app.Activity
import android.os.Bundle

// =============================================================================
// Mabel Host - Android Activity
// Activity minima que cria um MabelCanvasView e renderiza o app WASM.
// Funciona em Android 7+ (API 24).
// =============================================================================

/**
 * Entry-point Activity for Mabel apps on Android.
 * Creates a full-screen MabelCanvasView and loads the WASM module.
 *
 * Usage in AndroidManifest.xml:
 * ```xml
 * <activity android:name="com.mabel.host.MabelActivity"
 *           android:theme="@style/Theme.Mabel.NoActionBar">
 *     <intent-filter>
 *         <action android:name="android.intent.action.MAIN" />
 *         <category android:name="android.intent.category.LAUNCHER" />
 *     </intent-filter>
 * </activity>
 * ```
 */
class MabelActivity : Activity() {

    private lateinit var canvasView: MabelCanvasView
    private val engine = MabelEngine()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        canvasView = MabelCanvasView(this)
        setContentView(canvasView)

        // Load WASM module — for now, render a static hello world
        engine.load("app") { commands ->
            canvasView.commands = commands
        }
    }
}

/**
 * Engine that loads WASM modules and produces render commands.
 * TODO: integrate wasmtime-jni or wasm3 when available for Android.
 */
class MabelEngine {

    /**
     * Loads a WASM module by name and calls onFrame with render commands.
     * Currently produces a static demo frame.
     */
    fun load(wasmName: String, onFrame: (List<RenderCommand>) -> Unit) {
        // Placeholder: static Hello World
        onFrame(helloWorld())
    }

    companion object {
        /** Demo: renders a Hello World without WASM. */
        fun helloWorld(): List<RenderCommand> = listOf(
            RenderCommand(op = RenderOp.BEGIN_FRAME, color = 0x1A1A2EFFu),

            RenderCommand(op = RenderOp.ROUND_RECT,
                x = 40f, y = 100f, w = 300f, h = 200f,
                color = 0x16213EFFu, radius = 16f),

            RenderCommand(op = RenderOp.TEXT,
                x = 80f, y = 170f,
                color = 0xE94560FFu, text = "Mabel Framework", fontSize = 28f),

            RenderCommand(op = RenderOp.TEXT,
                x = 80f, y = 210f,
                color = 0x0F3460FFu, text = "Hello from WASI!", fontSize = 18f),

            RenderCommand(op = RenderOp.CIRCLE,
                x = 190f, y = 400f,
                color = 0xE94560FFu, radius = 40f),

            RenderCommand(op = RenderOp.END_FRAME),
        )

        /** Demo: renders a Glass Card with modern effects (iOS 26 / Material You style). */
        fun glassDemo(): List<RenderCommand> = listOf(
            RenderCommand(op = RenderOp.BEGIN_FRAME, color = 0x0A0A1AFFu),

            // Background gradient
            RenderCommand(op = RenderOp.LINEAR_GRAD,
                x = 0f, y = 0f, w = 0f, h = 812f,
                color = 0x1A1A3EFFu, color2 = 0x0A0A1AFFu),
            RenderCommand(op = RenderOp.RECT,
                x = 0f, y = 0f, w = 390f, h = 812f,
                color = 0x1A1A3EFFu),

            // Glass card with shadow
            RenderCommand(op = RenderOp.SHADOW,
                x = 0f, y = 8f,
                color = 0x00000060u, radius = 24f),
            RenderCommand(op = RenderOp.BLUR, radius = 40f),
            RenderCommand(op = RenderOp.LINEAR_GRAD,
                x = 40f, y = 120f, w = 40f, h = 320f,
                color = 0xFFFFFF30u, color2 = 0xFFFFFF10u),
            RenderCommand(op = RenderOp.ROUND_RECT,
                x = 40f, y = 120f, w = 310f, h = 200f,
                color = 0xFFFFFF18u, radius = 28f),

            // Glass card border
            RenderCommand(op = RenderOp.STROKE,
                x = 40f, y = 120f, w = 310f, h = 200f,
                color = 0xFFFFFF20u, radius = 28f, fontSize = 0.5f),

            // Card text
            RenderCommand(op = RenderOp.TEXT,
                x = 64f, y = 160f,
                color = 0xFFFFFFFFu, text = "Mabel Glass", fontSize = 32f),
            RenderCommand(op = RenderOp.TEXT,
                x = 64f, y = 200f,
                color = 0xFFFFFF99u, text = "Material You + Glass", fontSize = 16f),

            RenderCommand(op = RenderOp.END_FRAME),
        )
    }
}
