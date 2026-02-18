namespace Mabel.Wasi.Protocol;

/// <summary>
/// Render operations sent from Guest (WASM) to Host (native).
/// Binary flat format for performance. The host reads and draws via SkiaSharp/Canvas.
/// </summary>
public enum RenderOp : byte
{
    // Primitives
    Rect      = 0x01,
    RoundRect = 0x02,
    Circle    = 0x03,
    Line      = 0x04,
    Text      = 0x05,
    Image     = 0x06,

    // Effects (Glass / modern UI)
    Shadow       = 0x07,
    Blur         = 0x08,
    LinearGrad   = 0x09,
    RadialGrad   = 0x0A,
    Stroke       = 0x0B,
    Path         = 0x0C,

    // State
    PushClip    = 0x10,
    PopClip     = 0x11,
    PushOpacity = 0x12,
    PopOpacity  = 0x13,
    Translate   = 0x14,
    Scale       = 0x15,
    Rotate      = 0x16,

    // Layout
    BeginFrame = 0xF0,
    EndFrame   = 0xF1,
}

/// <summary>
/// A render command with its parameters.
///
/// Field semantics vary by <see cref="RenderOp"/>:
///
///   Op          | X      | Y      | W      | H      | Color        | Text         | Radius   | FontSize
///   ------------|--------|--------|--------|--------|--------------|--------------|----------|----------
///   BeginFrame  | -      | -      | -      | -      | bg color     | -            | -        | -
///   Rect        | left   | top    | width  | height | fill color   | -            | -        | -
///   RoundRect   | left   | top    | width  | height | fill color   | -            | corner r | -
///   Circle      | cx     | cy     | -      | -      | fill color   | -            | radius   | -
///   Line        | x1     | y1     | x2     | y2     | stroke color | -            | -        | strokeW
///   Text        | left   | top    | -      | -      | text color   | text content | -        | size
///   Image       | left   | top    | width  | height | -            | image id     | -        | -
///   Shadow      | offX   | offY   | -      | -      | shadow color | -            | blur r   | -
///   Blur        | -      | -      | -      | -      | -            | -            | blur r   | -
///   LinearGrad  | x1     | y1     | x2     | y2     | start color  | -            | -        | -
///   RadialGrad  | cx     | cy     | -      | -      | center color | -            | radius   | -
///   Stroke      | x      | y      | w      | h      | stroke color | -            | corner r | strokeW
///   Path        | -      | -      | -      | -      | fill color   | SVG path d   | -        | -
///   PushClip    | left   | top    | width  | height | -            | -            | -        | -
///   PopClip     | -      | -      | -      | -      | -            | -            | -        | -
///   PushOpacity | alpha  | -      | -      | -      | -            | -            | -        | -
///   PopOpacity  | -      | -      | -      | -      | -            | -            | -        | -
///   Translate   | dx     | dy     | -      | -      | -            | -            | -        | -
///   Scale       | sx     | sy     | -      | -      | -            | -            | -        | -
///   Rotate      | angle  | -      | -      | -      | -            | -            | -        | -
///   EndFrame    | -      | -      | -      | -      | -            | -            | -        | -
///
/// For Shadow: push shadow before drawing a shape. The next primitive drawn
/// will have the shadow applied. Call PopShadow (or draw another shape) to stop.
///
/// For Blur: applies Gaussian blur to subsequent draws until the next shape.
/// On iOS this maps to CIGaussianBlur; on SkiaSharp to SKImageFilter.CreateBlur.
///
/// For Gradients: LinearGrad/RadialGrad set the fill for the NEXT shape drawn.
/// Color = start/center color, Color2 = end/edge color.
///
/// For Stroke: draws an outline (not filled). FontSize field is reused as stroke width.
/// Radius is corner radius (0 = sharp corners).
///
/// For Path: Text field contains an SVG path data string (e.g., "M 10 10 L 50 50 Z").
/// Color = fill color. Use Stroke op after Path for stroked paths.
///
/// Color format: RGBA packed into a 32-bit unsigned integer (0xRRGGBBAA).
///   Red   = (color >> 24) &amp; 0xFF
///   Green = (color >> 16) &amp; 0xFF
///   Blue  = (color >>  8) &amp; 0xFF
///   Alpha = color &amp; 0xFF
///
/// Note: The <see cref="Text"/> field is a <c>string?</c>. For binary WASI transport,
/// this must be serialized separately (e.g., pointer + length in shared memory).
/// The current struct is designed for in-process use between the WASM runtime and
/// the renderer; binary serialization will use a flat buffer format without managed strings.
/// </summary>
public readonly record struct RenderCommand(
    RenderOp Op,
    float X,
    float Y,
    float W,
    float H,
    uint Color,
    string? Text = null,
    float Radius = 0,
    float FontSize = 14,
    uint Color2 = 0);

/// <summary>
/// Input events from Host to Guest.
/// </summary>
public enum InputEventType : byte
{
    TouchDown = 0x01,
    TouchUp   = 0x02,
    TouchMove = 0x03,
    KeyDown   = 0x04,
    KeyUp     = 0x05,
}

public readonly record struct InputEvent(InputEventType Type, float X, float Y, int KeyCode = 0);
