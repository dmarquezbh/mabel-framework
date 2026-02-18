namespace Mabel.Renderer;

/// <summary>
/// Abstracts the drawing canvas. Each platform implements:
/// - iOS: Core Graphics (CGContext)
/// - Android: android.graphics.Canvas
/// - Desktop: SkiaSharp SKCanvas
/// - Tests: FakeCanvas (records calls)
///
/// Color format: RGBA as a 32-bit unsigned integer (0xRRGGBBAA).
///   Red   = (color >> 24) &amp; 0xFF
///   Green = (color >> 16) &amp; 0xFF
///   Blue  = (color >>  8) &amp; 0xFF
///   Alpha = color &amp; 0xFF
/// </summary>
public interface ICanvas
{
    // -- Primitives --

    void DrawRect(float x, float y, float w, float h, uint color);
    void DrawRoundRect(float x, float y, float w, float h, float radius, uint color);
    void DrawCircle(float cx, float cy, float r, uint color);
    void DrawLine(float x1, float y1, float x2, float y2, uint color);
    void DrawText(string text, float x, float y, float fontSize, uint color);

    /// <summary>
    /// Draws an image identified by <paramref name="imageId"/> at the given position and size.
    /// Image loading/caching is platform-specific; the host resolves imageId to actual bitmap data.
    /// </summary>
    void DrawImage(string imageId, float x, float y, float w, float h);

    // -- Effects (Glass / modern UI) --

    /// <summary>
    /// Sets a drop shadow for subsequent draw operations.
    /// Call <see cref="ClearShadow"/> to stop applying it.
    /// On iOS: NSShadow / CGContext.setShadow.
    /// On SkiaSharp: SKPaint.ImageFilter = SKImageFilter.CreateDropShadow.
    /// </summary>
    void SetShadow(float offsetX, float offsetY, float blurRadius, uint color);

    /// <summary>
    /// Removes the current shadow.
    /// </summary>
    void ClearShadow();

    /// <summary>
    /// Applies a Gaussian blur to subsequent draw operations.
    /// On iOS: CIGaussianBlur via CIFilter.
    /// On SkiaSharp: SKImageFilter.CreateBlur.
    /// </summary>
    void SetBlur(float radius);

    /// <summary>
    /// Removes the current blur.
    /// </summary>
    void ClearBlur();

    /// <summary>
    /// Sets a linear gradient fill for the next shape drawn.
    /// The gradient goes from (x1,y1) with startColor to (x2,y2) with endColor.
    /// </summary>
    void SetLinearGradient(float x1, float y1, float x2, float y2, uint startColor, uint endColor);

    /// <summary>
    /// Sets a radial gradient fill for the next shape drawn.
    /// The gradient goes from centerColor at (cx,cy) to edgeColor at radius.
    /// </summary>
    void SetRadialGradient(float cx, float cy, float radius, uint centerColor, uint edgeColor);

    /// <summary>
    /// Clears the current gradient, reverting to solid color fills.
    /// </summary>
    void ClearGradient();

    /// <summary>
    /// Draws a stroked (outline) rectangle. Not filled.
    /// </summary>
    void DrawStrokeRect(float x, float y, float w, float h, float cornerRadius, float strokeWidth, uint color);

    /// <summary>
    /// Draws an SVG-style path.
    /// <paramref name="svgPathData"/> uses SVG path data syntax (e.g., "M 10 10 L 50 50 Z").
    /// </summary>
    void DrawPath(string svgPathData, uint color);

    // -- Measurement --

    float MeasureText(string text, float fontSize);

    // -- State management --

    void PushClip(float x, float y, float w, float h);
    void PopClip();
    void PushOpacity(float opacity);
    void PopOpacity();
    void Translate(float dx, float dy);

    /// <summary>
    /// Scales the canvas by (sx, sy) from the current origin.
    /// </summary>
    void Scale(float sx, float sy);

    /// <summary>
    /// Rotates the canvas by <paramref name="angleRadians"/> around the current origin.
    /// </summary>
    void Rotate(float angleRadians);

    void Clear(uint color);

    /// <summary>
    /// Saves the current graphics state (transform, clip, opacity, effects) onto a stack.
    /// Must be paired with <see cref="RestoreState"/>.
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restores the most recently saved graphics state.
    /// </summary>
    void RestoreState();
}
