namespace Mabel.Renderer;

/// <summary>
/// Abstrai o canvas de desenho. Cada plataforma implementa:
/// - iOS: Core Graphics (CGContext)
/// - Android: android.graphics.Canvas
/// - Desktop: SkiaSharp SKCanvas
/// - Testes: FakeCanvas (grava comandos)
///
/// Color format: RGBA as a 32-bit unsigned integer (0xRRGGBBAA).
///   Red   = (color >> 24) & 0xFF
///   Green = (color >> 16) & 0xFF
///   Blue  = (color >>  8) & 0xFF
///   Alpha = color & 0xFF
/// </summary>
public interface ICanvas
{
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

    float MeasureText(string text, float fontSize);
    void PushClip(float x, float y, float w, float h);
    void PopClip();
    void PushOpacity(float opacity);
    void PopOpacity();
    void Translate(float dx, float dy);
    void Clear(uint color);

    /// <summary>
    /// Saves the current graphics state (transform, clip, opacity) onto a stack.
    /// Must be paired with <see cref="RestoreState"/>.
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restores the most recently saved graphics state.
    /// </summary>
    void RestoreState();
}
