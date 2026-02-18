package com.mabel.host

import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView

// =============================================================================
// Mabel Host - Jetpack Compose integration
// Wrapper Composable que exibe o MabelCanvasView dentro de Jetpack Compose.
// =============================================================================

/**
 * Composable wrapper for MabelCanvasView.
 * Integrates with Jetpack Compose for modern Android apps.
 *
 * Usage:
 * ```kotlin
 * @Composable
 * fun MyApp() {
 *     MabelSurface(
 *         commands = remember { mutableStateOf(MabelEngine.helloWorld()) },
 *         modifier = Modifier.fillMaxSize()
 *     )
 * }
 * ```
 */
@Composable
fun MabelSurface(
    commands: List<RenderCommand>,
    modifier: Modifier = Modifier
) {
    AndroidView(
        factory = { context ->
            MabelCanvasView(context).apply {
                this.commands = commands
            }
        },
        update = { view ->
            view.commands = commands
            view.invalidate()
        },
        modifier = modifier
    )
}

/**
 * Composable that loads a WASM module and renders it.
 * Self-contained — handles engine lifecycle internally.
 *
 * Usage:
 * ```kotlin
 * @Composable
 * fun MyApp() {
 *     MabelApp(wasmName = "app", modifier = Modifier.fillMaxSize())
 * }
 * ```
 */
@Composable
fun MabelApp(
    wasmName: String = "app",
    modifier: Modifier = Modifier
) {
    val commands = remember { mutableStateOf(emptyList<RenderCommand>()) }
    val engine = remember { MabelEngine() }

    // Load WASM on first composition
    androidx.compose.runtime.LaunchedEffect(wasmName) {
        engine.load(wasmName) { cmds ->
            commands.value = cmds
        }
    }

    MabelSurface(
        commands = commands.value,
        modifier = modifier
    )
}
