namespace Mabel.Wasi.Protocol;

/// <summary>
/// Comandos de renderizacao enviados do Guest (WASM) para o Host (nativo).
/// Formato binario flat para performance. O host le e desenha via SkiaSharp/Canvas.
/// </summary>
public enum RenderOp : byte
{
    // Primitivas
    Rect      = 0x01,
    RoundRect = 0x02,
    Circle    = 0x03,
    Line      = 0x04,
    Text      = 0x05,
    Image     = 0x06,

    // State
    PushClip    = 0x10,
    PopClip     = 0x11,
    PushOpacity = 0x12,
    PopOpacity  = 0x13,
    Translate   = 0x14,

    // Layout
    BeginFrame = 0xF0,
    EndFrame   = 0xF1,
}

/// <summary>
/// Um comando de render com seus parametros.
///
/// Field semantics vary by <see cref="RenderOp"/>:
///
///   Op          | X      | Y      | W      | H      | Color        | Text         | Radius   | FontSize
///   ------------|--------|--------|--------|--------|--------------|--------------|----------|----------
///   BeginFrame  | -      | -      | -      | -      | bg color     | -            | -        | -
///   Rect        | left   | top    | width  | height | fill color   | -            | -        | -
///   RoundRect   | left   | top    | width  | height | fill color   | -            | corner r | -
///   Circle      | cx     | cy     | -      | -      | fill color   | -            | radius   | -
///   Line        | x1     | y1     | x2     | y2     | stroke color | -            | -        | -
///   Text        | left   | top    | -      | -      | text color   | text content | -        | size
///   Image       | left   | top    | width  | height | -            | image id     | -        | -
///   PushClip    | left   | top    | width  | height | -            | -            | -        | -
///   PopClip     | -      | -      | -      | -      | -            | -            | -        | -
///   PushOpacity | alpha  | -      | -      | -      | -            | -            | -        | -
///   PopOpacity  | -      | -      | -      | -      | -            | -            | -        | -
///   Translate   | dx     | dy     | -      | -      | -            | -            | -        | -
///   EndFrame    | -      | -      | -      | -      | -            | -            | -        | -
///
/// Color format: RGBA packed into a 32-bit unsigned integer (0xRRGGBBAA).
///   Red   = (color >> 24) & 0xFF
///   Green = (color >> 16) & 0xFF
///   Blue  = (color >>  8) & 0xFF
///   Alpha = color & 0xFF
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
    float FontSize = 14);

/// <summary>
/// Eventos de input do Host para o Guest.
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
