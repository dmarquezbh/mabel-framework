package com.mabel.host

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.View
import kotlin.math.cos
import kotlin.math.sin

// =============================================================================
// Mabel Host - Android
// Carrega um modulo WASM/WASI e renderiza via android.graphics.Canvas (sem WebView).
// Suporta todas as operacoes Glass / modern UI.
//
// Plataforma: Android 7+ (API 24)
// Canvas API: android.graphics.Canvas + Paint + Path + Shader + PorterDuff
// =============================================================================

/**
 * Render operations — mirrors Mabel.Wasi.Protocol.RenderOp.
 * Same byte values across all platforms (iOS, Android, Desktop, Web).
 */
object RenderOp {
    // Primitives
    const val RECT: Byte        = 0x01
    const val ROUND_RECT: Byte  = 0x02
    const val CIRCLE: Byte      = 0x03
    const val LINE: Byte        = 0x04
    const val TEXT: Byte        = 0x05
    const val IMAGE: Byte       = 0x06

    // Effects (Glass / modern UI)
    const val SHADOW: Byte      = 0x07
    const val BLUR: Byte        = 0x08
    const val LINEAR_GRAD: Byte = 0x09
    const val RADIAL_GRAD: Byte = 0x0A
    const val STROKE: Byte      = 0x0B
    const val PATH: Byte        = 0x0C

    // State
    const val PUSH_CLIP: Byte    = 0x10
    const val POP_CLIP: Byte     = 0x11
    const val PUSH_OPACITY: Byte = 0x12
    const val POP_OPACITY: Byte  = 0x13
    const val TRANSLATE: Byte    = 0x14
    const val SCALE: Byte        = 0x15
    const val ROTATE: Byte       = 0x16

    // Layout
    const val BEGIN_FRAME: Byte = 0xF0.toByte()
    const val END_FRAME: Byte   = 0xF1.toByte()
}

/**
 * A render command with its parameters.
 * Field semantics vary by op — see Protocol.cs documentation for full table.
 *
 * Color format: RGBA packed into UInt (0xRRGGBBAA).
 * Android uses ARGB internally, so we convert on draw.
 */
data class RenderCommand(
    val op: Byte,
    val x: Float = 0f,
    val y: Float = 0f,
    val w: Float = 0f,
    val h: Float = 0f,
    val color: UInt = 0u,
    val text: String? = null,
    val radius: Float = 0f,
    val fontSize: Float = 14f,
    val color2: UInt = 0u
)

/**
 * Custom View that renders RenderCommands via android.graphics.Canvas.
 * Lightweight, works on Android 7+ (API 24), no WebView.
 *
 * Supports Glass operations: Shadow, Blur, LinearGradient, RadialGradient,
 * Stroke, Path, Scale, Rotate. Same binary protocol as all other platforms.
 *
 * Usage:
 * ```kotlin
 * val canvasView = MabelCanvasView(context)
 * canvasView.commands = listOf(...)
 * canvasView.invalidate()
 * ```
 *
 * For Jetpack Compose, wrap with AndroidView:
 * ```kotlin
 * AndroidView(factory = { MabelCanvasView(it) }) { view ->
 *     view.commands = commands
 *     view.invalidate()
 * }
 * ```
 */
class MabelCanvasView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    /** The list of render commands to draw. Set this and call invalidate(). */
    var commands: List<RenderCommand> = emptyList()
        set(value) {
            field = value
            invalidate()
        }

    // Reusable paint objects to avoid allocation per frame
    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
    }
    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
    }

    // Effect state (set by effect ops, consumed by next shape)
    private var pendingShadow: ShadowState? = null
    private var pendingBlurRadius: Float? = null
    private var pendingGradient: GradientState? = null

    private data class ShadowState(
        val offsetX: Float,
        val offsetY: Float,
        val blurRadius: Float,
        val color: Int
    )

    private sealed class GradientState {
        data class Linear(
            val x1: Float, val y1: Float,
            val x2: Float, val y2: Float,
            val startColor: Int, val endColor: Int
        ) : GradientState()

        data class Radial(
            val cx: Float, val cy: Float,
            val radius: Float,
            val centerColor: Int, val edgeColor: Int
        ) : GradientState()
    }

    // Reusable RectF to avoid allocation
    private val tempRect = RectF()

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        // Reset effect state at frame start
        pendingShadow = null
        pendingBlurRadius = null
        pendingGradient = null

        for (cmd in commands) {
            when (cmd.op) {

                // -- Frame --

                RenderOp.BEGIN_FRAME -> {
                    canvas.save()
                    fillPaint.shader = null
                    fillPaint.color = rgbaToArgb(cmd.color)
                    canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), fillPaint)
                }

                RenderOp.END_FRAME -> {
                    canvas.restore()
                    pendingShadow = null
                    pendingBlurRadius = null
                    pendingGradient = null
                }

                // -- Primitives --

                RenderOp.RECT -> {
                    applyEffects(fillPaint)
                    fillPaint.style = Paint.Style.FILL
                    fillPaint.color = rgbaToArgb(cmd.color)
                    applyGradientShader(fillPaint)
                    tempRect.set(cmd.x, cmd.y, cmd.x + cmd.w, cmd.y + cmd.h)
                    canvas.drawRect(tempRect, fillPaint)
                    clearEffects(fillPaint)
                }

                RenderOp.ROUND_RECT -> {
                    applyEffects(fillPaint)
                    fillPaint.style = Paint.Style.FILL
                    fillPaint.color = rgbaToArgb(cmd.color)
                    applyGradientShader(fillPaint)
                    tempRect.set(cmd.x, cmd.y, cmd.x + cmd.w, cmd.y + cmd.h)
                    canvas.drawRoundRect(tempRect, cmd.radius, cmd.radius, fillPaint)
                    clearEffects(fillPaint)
                }

                RenderOp.CIRCLE -> {
                    applyEffects(fillPaint)
                    fillPaint.style = Paint.Style.FILL
                    fillPaint.color = rgbaToArgb(cmd.color)
                    applyGradientShader(fillPaint)
                    canvas.drawCircle(cmd.x, cmd.y, cmd.radius, fillPaint)
                    clearEffects(fillPaint)
                }

                RenderOp.LINE -> {
                    strokePaint.color = rgbaToArgb(cmd.color)
                    strokePaint.strokeWidth = if (cmd.fontSize > 0) cmd.fontSize else 1f
                    canvas.drawLine(cmd.x, cmd.y, cmd.w, cmd.h, strokePaint)
                }

                RenderOp.TEXT -> {
                    textPaint.color = rgbaToArgb(cmd.color)
                    textPaint.textSize = cmd.fontSize * resources.displayMetrics.density
                    textPaint.typeface = Typeface.DEFAULT
                    canvas.drawText(cmd.text ?: "", cmd.x, cmd.y + cmd.fontSize, textPaint)
                }

                RenderOp.IMAGE -> {
                    // Image rendering — requires asset loading from resources or cache
                    // TODO: integrate with asset manager
                }

                // -- Effects (Glass / modern UI) --

                RenderOp.SHADOW -> {
                    pendingShadow = ShadowState(
                        offsetX = cmd.x,
                        offsetY = cmd.y,
                        blurRadius = cmd.radius,
                        color = rgbaToArgb(cmd.color)
                    )
                }

                RenderOp.BLUR -> {
                    pendingBlurRadius = cmd.radius
                }

                RenderOp.LINEAR_GRAD -> {
                    pendingGradient = GradientState.Linear(
                        x1 = cmd.x, y1 = cmd.y,
                        x2 = cmd.w, y2 = cmd.h,
                        startColor = rgbaToArgb(cmd.color),
                        endColor = rgbaToArgb(cmd.color2)
                    )
                }

                RenderOp.RADIAL_GRAD -> {
                    pendingGradient = GradientState.Radial(
                        cx = cmd.x, cy = cmd.y,
                        radius = cmd.radius,
                        centerColor = rgbaToArgb(cmd.color),
                        edgeColor = rgbaToArgb(cmd.color2)
                    )
                }

                RenderOp.STROKE -> {
                    applyEffects(strokePaint)
                    strokePaint.color = rgbaToArgb(cmd.color)
                    strokePaint.strokeWidth = if (cmd.fontSize > 0) cmd.fontSize else 1f
                    tempRect.set(cmd.x, cmd.y, cmd.x + cmd.w, cmd.y + cmd.h)
                    if (cmd.radius > 0) {
                        canvas.drawRoundRect(tempRect, cmd.radius, cmd.radius, strokePaint)
                    } else {
                        canvas.drawRect(tempRect, strokePaint)
                    }
                    clearEffects(strokePaint)
                }

                RenderOp.PATH -> {
                    val pathData = cmd.text
                    if (!pathData.isNullOrEmpty()) {
                        val androidPath = parseSvgPath(pathData)
                        if (androidPath != null) {
                            applyEffects(fillPaint)
                            fillPaint.style = Paint.Style.FILL
                            fillPaint.color = rgbaToArgb(cmd.color)
                            applyGradientShader(fillPaint)
                            canvas.drawPath(androidPath, fillPaint)
                            clearEffects(fillPaint)
                        }
                    }
                }

                // -- State --

                RenderOp.PUSH_CLIP -> {
                    canvas.save()
                    tempRect.set(cmd.x, cmd.y, cmd.x + cmd.w, cmd.y + cmd.h)
                    canvas.clipRect(tempRect)
                }

                RenderOp.POP_CLIP -> {
                    canvas.restore()
                }

                RenderOp.PUSH_OPACITY -> {
                    canvas.save()
                    val alpha = (cmd.x * 255).toInt().coerceIn(0, 255)
                    canvas.saveLayerAlpha(
                        0f, 0f, width.toFloat(), height.toFloat(), alpha
                    )
                }

                RenderOp.POP_OPACITY -> {
                    canvas.restore()
                }

                RenderOp.TRANSLATE -> {
                    canvas.translate(cmd.x, cmd.y)
                }

                RenderOp.SCALE -> {
                    canvas.scale(cmd.x, cmd.y)
                }

                RenderOp.ROTATE -> {
                    // Convert radians to degrees (Android Canvas uses degrees)
                    canvas.rotate(Math.toDegrees(cmd.x.toDouble()).toFloat())
                }
            }
        }
    }

    // =========================================================================
    // Effect helpers
    // =========================================================================

    /**
     * Applies pending shadow and blur effects to a paint before drawing a shape.
     */
    private fun applyEffects(paint: Paint) {
        // Shadow
        pendingShadow?.let { shadow ->
            // setLayerType is required for shadow to render in hardware acceleration
            setLayerType(LAYER_TYPE_SOFTWARE, paint)
            paint.setShadowLayer(
                shadow.blurRadius,
                shadow.offsetX,
                shadow.offsetY,
                shadow.color
            )
        }

        // Blur: MaskFilter for shape blur
        pendingBlurRadius?.let { radius ->
            if (radius > 0) {
                paint.maskFilter = BlurMaskFilter(radius, BlurMaskFilter.Blur.NORMAL)
            }
        }
    }

    /**
     * Applies gradient shader to paint if a gradient is pending.
     */
    private fun applyGradientShader(paint: Paint) {
        when (val grad = pendingGradient) {
            is GradientState.Linear -> {
                paint.shader = LinearGradient(
                    grad.x1, grad.y1, grad.x2, grad.y2,
                    grad.startColor, grad.endColor,
                    Shader.TileMode.CLAMP
                )
            }
            is GradientState.Radial -> {
                paint.shader = RadialGradient(
                    grad.cx, grad.cy, grad.radius.coerceAtLeast(0.001f),
                    grad.centerColor, grad.edgeColor,
                    Shader.TileMode.CLAMP
                )
            }
            null -> {
                paint.shader = null
            }
        }
    }

    /**
     * Clears effects from paint after drawing.
     */
    private fun clearEffects(paint: Paint) {
        paint.clearShadowLayer()
        paint.maskFilter = null
        paint.shader = null
    }

    // =========================================================================
    // SVG Path parsing (subset)
    // =========================================================================

    /**
     * Parses a subset of SVG path data (M, L, C, Q, H, V, Z commands).
     * Sufficient for common Glass UI shapes.
     */
    private fun parseSvgPath(data: String): Path? {
        val path = Path()
        var i = 0
        val len = data.length
        var cx = 0f
        var cy = 0f

        fun skipWhitespaceAndCommas() {
            while (i < len && (data[i] == ' ' || data[i] == ',' || data[i] == '\n' || data[i] == '\r' || data[i] == '\t')) {
                i++
            }
        }

        fun parseFloat(): Float? {
            skipWhitespaceAndCommas()
            if (i >= len) return null
            val start = i
            if (i < len && (data[i] == '-' || data[i] == '+')) i++
            while (i < len && (data[i] in '0'..'9' || data[i] == '.')) i++
            if (i == start) return null
            return data.substring(start, i).toFloatOrNull()
        }

        while (i < len) {
            skipWhitespaceAndCommas()
            if (i >= len) break

            val cmd = data[i]
            if (!cmd.isLetter()) { i++; continue }
            i++

            when (cmd) {
                'M' -> {
                    val x = parseFloat() ?: break
                    val y = parseFloat() ?: break
                    path.moveTo(x, y)
                    cx = x; cy = y
                }
                'm' -> {
                    val dx = parseFloat() ?: break
                    val dy = parseFloat() ?: break
                    path.rMoveTo(dx, dy)
                    cx += dx; cy += dy
                }
                'L' -> {
                    val x = parseFloat() ?: break
                    val y = parseFloat() ?: break
                    path.lineTo(x, y)
                    cx = x; cy = y
                }
                'l' -> {
                    val dx = parseFloat() ?: break
                    val dy = parseFloat() ?: break
                    path.rLineTo(dx, dy)
                    cx += dx; cy += dy
                }
                'H' -> {
                    val x = parseFloat() ?: break
                    path.lineTo(x, cy)
                    cx = x
                }
                'h' -> {
                    val dx = parseFloat() ?: break
                    path.rLineTo(dx, 0f)
                    cx += dx
                }
                'V' -> {
                    val y = parseFloat() ?: break
                    path.lineTo(cx, y)
                    cy = y
                }
                'v' -> {
                    val dy = parseFloat() ?: break
                    path.rLineTo(0f, dy)
                    cy += dy
                }
                'C' -> {
                    val x1 = parseFloat() ?: break
                    val y1 = parseFloat() ?: break
                    val x2 = parseFloat() ?: break
                    val y2 = parseFloat() ?: break
                    val x = parseFloat() ?: break
                    val y = parseFloat() ?: break
                    path.cubicTo(x1, y1, x2, y2, x, y)
                    cx = x; cy = y
                }
                'Q' -> {
                    val x1 = parseFloat() ?: break
                    val y1 = parseFloat() ?: break
                    val x = parseFloat() ?: break
                    val y = parseFloat() ?: break
                    path.quadTo(x1, y1, x, y)
                    cx = x; cy = y
                }
                'Z', 'z' -> {
                    path.close()
                }
            }
        }

        return if (path.isEmpty) null else path
    }

    // =========================================================================
    // Color conversion
    // =========================================================================

    /**
     * Converts packed RGBA (0xRRGGBBAA) to Android's ARGB int.
     * Mabel protocol: RGBA — Red in bits 31-24, Alpha in bits 7-0.
     * Android Canvas: ARGB — Alpha in bits 31-24, Red in bits 23-16.
     */
    private fun rgbaToArgb(rgba: UInt): Int {
        val r = ((rgba shr 24) and 0xFFu).toInt()
        val g = ((rgba shr 16) and 0xFFu).toInt()
        val b = ((rgba shr 8) and 0xFFu).toInt()
        val a = (rgba and 0xFFu).toInt()
        return (a shl 24) or (r shl 16) or (g shl 8) or b
    }
}
