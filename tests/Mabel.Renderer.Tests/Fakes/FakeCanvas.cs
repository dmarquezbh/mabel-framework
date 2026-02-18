using Mabel.Renderer;
using Mabel.Wasi.Protocol;

namespace Mabel.Renderer.Tests.Fakes;

/// <summary>
/// FakeCanvas records all draw calls for assertion in tests.
/// </summary>
public sealed class FakeCanvas : ICanvas
{
    public record DrawCall(string Method, float X, float Y, float W, float H, uint Color, string? Text = null, float FontSize = 0, float Radius = 0);

    private readonly List<DrawCall> _calls = new();

    public IReadOnlyList<DrawCall> Calls => _calls;
    public uint? LastClearColor { get; private set; }
    public int ClipDepth { get; private set; }
    public int OpacityDepth { get; private set; }
    public float TranslateX { get; private set; }
    public float TranslateY { get; private set; }
    public int StateDepth { get; private set; }

    // Stack to support SaveState/RestoreState
    private readonly Stack<(float tx, float ty, int clip, int opacity)> _stateStack = new();

    public void DrawRect(float x, float y, float w, float h, uint color)
        => _calls.Add(new("DrawRect", x, y, w, h, color));

    public void DrawRoundRect(float x, float y, float w, float h, float radius, uint color)
        => _calls.Add(new("DrawRoundRect", x, y, w, h, color, Radius: radius));

    public void DrawCircle(float cx, float cy, float r, uint color)
        => _calls.Add(new("DrawCircle", cx, cy, 0, 0, color, Radius: r));

    public void DrawLine(float x1, float y1, float x2, float y2, uint color)
        => _calls.Add(new("DrawLine", x1, y1, x2, y2, color));

    public void DrawText(string text, float x, float y, float fontSize, uint color)
        => _calls.Add(new("DrawText", x, y, 0, 0, color, Text: text, FontSize: fontSize));

    public void DrawImage(string imageId, float x, float y, float w, float h)
        => _calls.Add(new("DrawImage", x, y, w, h, 0, Text: imageId));

    // -- Effects (Glass / modern UI) --

    public void SetShadow(float offsetX, float offsetY, float blurRadius, uint color)
        => _calls.Add(new("SetShadow", offsetX, offsetY, blurRadius, 0, color));

    public void ClearShadow()
        => _calls.Add(new("ClearShadow", 0, 0, 0, 0, 0));

    public void SetBlur(float radius)
        => _calls.Add(new("SetBlur", radius, 0, 0, 0, 0));

    public void ClearBlur()
        => _calls.Add(new("ClearBlur", 0, 0, 0, 0, 0));

    public void SetLinearGradient(float x1, float y1, float x2, float y2, uint startColor, uint endColor)
        => _calls.Add(new("SetLinearGradient", x1, y1, x2, y2, startColor));

    public void SetRadialGradient(float cx, float cy, float radius, uint centerColor, uint edgeColor)
        => _calls.Add(new("SetRadialGradient", cx, cy, radius, 0, centerColor));

    public void ClearGradient()
        => _calls.Add(new("ClearGradient", 0, 0, 0, 0, 0));

    public void DrawStrokeRect(float x, float y, float w, float h, float cornerRadius, float strokeWidth, uint color)
        => _calls.Add(new("DrawStrokeRect", x, y, w, h, color, Radius: cornerRadius, FontSize: strokeWidth));

    public void DrawPath(string svgPathData, uint color)
        => _calls.Add(new("DrawPath", 0, 0, 0, 0, color, Text: svgPathData));

    // -- Measurement --

    public float MeasureText(string text, float fontSize)
        => text.Length * fontSize * 0.6f; // Approximate for testing

    public void PushClip(float x, float y, float w, float h)
    {
        ClipDepth++;
        _calls.Add(new("PushClip", x, y, w, h, 0));
    }

    public void PopClip()
    {
        ClipDepth--;
        _calls.Add(new("PopClip", 0, 0, 0, 0, 0));
    }

    public void PushOpacity(float opacity)
    {
        OpacityDepth++;
        _calls.Add(new("PushOpacity", opacity, 0, 0, 0, 0));
    }

    public void PopOpacity()
    {
        OpacityDepth--;
        _calls.Add(new("PopOpacity", 0, 0, 0, 0, 0));
    }

    public void Translate(float dx, float dy)
    {
        TranslateX += dx;
        TranslateY += dy;
        _calls.Add(new("Translate", dx, dy, 0, 0, 0));
    }

    public void Scale(float sx, float sy)
        => _calls.Add(new("Scale", sx, sy, 0, 0, 0));

    public void Rotate(float angleRadians)
        => _calls.Add(new("Rotate", angleRadians, 0, 0, 0, 0));

    public void Clear(uint color)
    {
        LastClearColor = color;
        _calls.Add(new("Clear", 0, 0, 0, 0, color));
    }

    public void SaveState()
    {
        StateDepth++;
        _stateStack.Push((TranslateX, TranslateY, ClipDepth, OpacityDepth));
        _calls.Add(new("SaveState", 0, 0, 0, 0, 0));
    }

    public void RestoreState()
    {
        StateDepth--;
        if (_stateStack.Count > 0)
        {
            var (tx, ty, clip, opacity) = _stateStack.Pop();
            TranslateX = tx;
            TranslateY = ty;
            ClipDepth = clip;
            OpacityDepth = opacity;
        }
        _calls.Add(new("RestoreState", 0, 0, 0, 0, 0));
    }
}
