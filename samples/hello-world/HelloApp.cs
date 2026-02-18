using Mabel.Wasi.Protocol;

namespace MabelHelloWorld;

/// <summary>
/// A minimal Mabel application that renders "Hello, Mabel!" on a canvas.
///
/// This demonstrates the core Mabel rendering pipeline:
///   1. Build a list of RenderCommands
///   2. The renderer interprets them and draws on an ICanvas
///   3. The host (iOS/Desktop) provides the actual canvas implementation
///
/// In production, this code would be compiled to WASM/WASI and loaded
/// by the native host. For this sample, we run it as a console app
/// that prints the render commands to stdout.
/// </summary>
public static class HelloApp
{
    // Colors (RGBA packed as uint32: 0xRRGGBBAA)
    private const uint White      = 0xFFFFFFFF;
    private const uint DarkBlue   = 0x1A1A2EFF;
    private const uint Purple     = 0x6C63FFFF;
    private const uint LightGray  = 0xCCCCCCFF;
    private const uint Coral      = 0xFF6B6BFF;
    private const uint Teal       = 0x00C9A7FF;

    // Helper: shorthand for common command patterns
    private static RenderCommand Frame(RenderOp op, uint color = 0)
        => new(op, 0, 0, 0, 0, color);

    private static RenderCommand Rect(float x, float y, float w, float h, uint color)
        => new(RenderOp.Rect, x, y, w, h, color);

    private static RenderCommand RoundRect(float x, float y, float w, float h, uint color, float radius)
        => new(RenderOp.RoundRect, x, y, w, h, color, Radius: radius);

    private static RenderCommand Circle(float cx, float cy, uint color, float radius)
        => new(RenderOp.Circle, cx, cy, 0, 0, color, Radius: radius);

    private static RenderCommand Text(float x, float y, string text, float fontSize, uint color)
        => new(RenderOp.Text, x, y, 0, 0, color, Text: text, FontSize: fontSize);

    /// <summary>
    /// Builds the render commands for one frame of the hello world app.
    /// </summary>
    public static RenderCommand[] BuildFrame(float screenWidth, float screenHeight)
    {
        var cx = screenWidth / 2f;
        var commands = new List<RenderCommand>();

        // -- Begin frame with dark background --
        commands.Add(Frame(RenderOp.BeginFrame, DarkBlue));

        // -- Header bar --
        commands.Add(Rect(0, 0, screenWidth, 80, Purple));
        commands.Add(Text(cx - 80, 30, "Mabel Framework", 24, White));

        // -- Main greeting --
        commands.Add(Text(cx - 100, 160, "Hello, Mabel!", 36, White));
        commands.Add(Text(cx - 140, 210, "Blazor + WASI + Native Canvas", 18, LightGray));

        // -- Decorative cards --

        // Card 1: No WebView
        commands.Add(RoundRect(40, 280, screenWidth - 80, 120, Teal, 16));
        commands.Add(Text(60, 310, "No WebView", 20, DarkBlue));
        commands.Add(Text(60, 345, "Pure native canvas rendering via WASI protocol", 14, DarkBlue));

        // Card 2: Hot Reload
        commands.Add(RoundRect(40, 420, screenWidth - 80, 120, Coral, 16));
        commands.Add(Text(60, 450, "Hot Reload", 20, White));
        commands.Add(Text(60, 485, "Edit code, save, see changes instantly on device", 14, White));

        // -- Decorative circles --
        commands.Add(Circle(80, 600, Purple, 30));
        commands.Add(Circle(cx, 600, Coral, 20));
        commands.Add(Circle(screenWidth - 80, 600, Teal, 30));

        // -- Footer --
        commands.Add(Text(cx - 60, screenHeight - 40, "mabel v0.1.0-dev", 12, LightGray));

        // -- End frame --
        commands.Add(Frame(RenderOp.EndFrame));

        return commands.ToArray();
    }

    /// <summary>
    /// Prints the render commands to stdout for debugging/inspection.
    /// In a real Mabel app, these would be sent to the native host via WASI.
    /// </summary>
    public static void PrintFrame(RenderCommand[] commands)
    {
        Console.WriteLine("=== Mabel Render Frame ===");
        Console.WriteLine($"  Commands: {commands.Length}");
        Console.WriteLine();

        foreach (var cmd in commands)
        {
            var details = cmd.Op switch
            {
                RenderOp.BeginFrame => $"background=#{cmd.Color:X8}",
                RenderOp.Rect       => $"x={cmd.X} y={cmd.Y} w={cmd.W} h={cmd.H} color=#{cmd.Color:X8}",
                RenderOp.RoundRect  => $"x={cmd.X} y={cmd.Y} w={cmd.W} h={cmd.H} r={cmd.Radius} color=#{cmd.Color:X8}",
                RenderOp.Circle     => $"cx={cmd.X} cy={cmd.Y} r={cmd.Radius} color=#{cmd.Color:X8}",
                RenderOp.Text       => $"x={cmd.X} y={cmd.Y} size={cmd.FontSize} color=#{cmd.Color:X8} \"{cmd.Text}\"",
                RenderOp.EndFrame   => "",
                _                   => cmd.ToString(),
            };

            Console.WriteLine($"  [{cmd.Op,-12}] {details}");
        }

        Console.WriteLine();
        Console.WriteLine("=== End Frame ===");
    }
}
