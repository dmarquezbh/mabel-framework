using Mabel.Renderer;
using Mabel.Renderer.Tests.Fakes;
using Mabel.Wasi.Protocol;
using Xunit;

namespace Mabel.Renderer.Tests;

public class MabelRendererTests
{
    [Fact]
    public void BeginFrame_SavesStateAndClearsCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.BeginFrame, 0, 0, 0, 0, 0xFF000000)]);

        Assert.Equal(0xFF000000u, canvas.LastClearColor);
        // BeginFrame produces SaveState + Clear
        Assert.Equal(2, canvas.Calls.Count);
        Assert.Equal("SaveState", canvas.Calls[0].Method);
        Assert.Equal("Clear", canvas.Calls[1].Method);
    }

    [Fact]
    public void Rect_DrawsOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Rect, 10, 20, 100, 50, 0xFFFF0000)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawRect", call.Method);
        Assert.Equal(10f, call.X);
        Assert.Equal(20f, call.Y);
        Assert.Equal(100f, call.W);
        Assert.Equal(50f, call.H);
        Assert.Equal(0xFFFF0000u, call.Color);
    }

    [Fact]
    public void RoundRect_IncludesRadius()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.RoundRect, 0, 0, 200, 100, 0xFF00FF00, Radius: 12)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawRoundRect", call.Method);
        Assert.Equal(12f, call.Radius);
    }

    [Fact]
    public void Circle_UsesRadiusFromCommand()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Circle, 50, 50, 0, 0, 0xFF0000FF, Radius: 25)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawCircle", call.Method);
        Assert.Equal(50f, call.X);
        Assert.Equal(50f, call.Y);
        Assert.Equal(25f, call.Radius);
    }

    [Fact]
    public void Text_DrawsWithFontSize()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Text, 10, 40, 0, 0, 0xFFFFFFFF, Text: "Hello Mabel", FontSize: 24)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawText", call.Method);
        Assert.Equal("Hello Mabel", call.Text);
        Assert.Equal(24f, call.FontSize);
    }

    [Fact]
    public void Text_NullText_DrawsEmptyString()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Text, 0, 0, 0, 0, 0xFFFFFFFF, Text: null)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("", call.Text);
    }

    [Fact]
    public void Line_DrawsOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Line, 0, 0, 100, 100, 0xFF888888)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawLine", call.Method);
    }

    [Fact]
    public void Image_DrawsOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Image, 10, 20, 64, 64, 0, Text: "logo.png")]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawImage", call.Method);
        Assert.Equal("logo.png", call.Text);
        Assert.Equal(10f, call.X);
        Assert.Equal(20f, call.Y);
        Assert.Equal(64f, call.W);
        Assert.Equal(64f, call.H);
    }

    [Fact]
    public void Image_NullText_UsesEmptyString()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Image, 0, 0, 32, 32, 0, Text: null)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawImage", call.Method);
        Assert.Equal("", call.Text);
    }

    [Fact]
    public void PushPopClip_ManagesClipDepth()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.PushClip, 0, 0, 100, 100, 0),
            new RenderCommand(RenderOp.Rect, 10, 10, 80, 80, 0xFFFFFFFF),
            new RenderCommand(RenderOp.PopClip, 0, 0, 0, 0, 0),
        ]);

        Assert.Equal(0, canvas.ClipDepth);
        Assert.Equal(3, canvas.Calls.Count);
        Assert.Equal("PushClip", canvas.Calls[0].Method);
        Assert.Equal("DrawRect", canvas.Calls[1].Method);
        Assert.Equal("PopClip", canvas.Calls[2].Method);
    }

    [Fact]
    public void PushPopOpacity_ManagesDepth()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.PushOpacity, 0.5f, 0, 0, 0, 0),
            new RenderCommand(RenderOp.PopOpacity, 0, 0, 0, 0, 0),
        ]);

        Assert.Equal(0, canvas.OpacityDepth);
    }

    [Fact]
    public void Translate_AccumulatesOffset()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.Translate, 10, 20, 0, 0, 0),
            new RenderCommand(RenderOp.Translate, 5, 3, 0, 0, 0),
        ]);

        Assert.Equal(15f, canvas.TranslateX);
        Assert.Equal(23f, canvas.TranslateY);
    }

    [Fact]
    public void FullFrame_RendersMultipleCommands()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.BeginFrame, 0, 0, 0, 0, 0xFF222222),
            new RenderCommand(RenderOp.Rect, 0, 0, 375, 812, 0xFF333333),
            new RenderCommand(RenderOp.Text, 20, 100, 0, 0, 0xFFFFFFFF, Text: "Hello World", FontSize: 32),
            new RenderCommand(RenderOp.Circle, 187, 400, 0, 0, 0xFF00AAFF, Radius: 60),
            new RenderCommand(RenderOp.EndFrame, 0, 0, 0, 0, 0),
        ]);

        Assert.Equal(0xFF222222u, canvas.LastClearColor);
        // SaveState + Clear + Rect + Text + Circle + RestoreState = 6 calls
        Assert.Equal(6, canvas.Calls.Count);
        Assert.Equal("SaveState", canvas.Calls[0].Method);
        Assert.Equal("Clear", canvas.Calls[1].Method);
        Assert.Equal("DrawRect", canvas.Calls[2].Method);
        Assert.Equal("DrawText", canvas.Calls[3].Method);
        Assert.Equal("DrawCircle", canvas.Calls[4].Method);
        Assert.Equal("RestoreState", canvas.Calls[5].Method);
    }

    [Fact]
    public void EndFrame_RestoresState()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.EndFrame, 0, 0, 0, 0, 0)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("RestoreState", call.Method);
    }

    [Fact]
    public void BeginEndFrame_ResetsTranslate()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.BeginFrame, 0, 0, 0, 0, 0xFF000000),
            new RenderCommand(RenderOp.Translate, 100, 200, 0, 0, 0),
            new RenderCommand(RenderOp.EndFrame, 0, 0, 0, 0, 0),
        ]);

        // After EndFrame, translate should be reset to 0,0 (restored to pre-BeginFrame state)
        Assert.Equal(0f, canvas.TranslateX);
        Assert.Equal(0f, canvas.TranslateY);
    }

    [Fact]
    public void UnknownOp_IsIgnored()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        // Cast an invalid byte to RenderOp to simulate a future/unknown op
        var unknownOp = (RenderOp)0xFE;
        renderer.Render([new RenderCommand(unknownOp, 1, 2, 3, 4, 0xFFFFFFFF)]);

        Assert.Empty(canvas.Calls);
    }

    // ========================================================================
    // Glass / Effects operations
    // ========================================================================

    [Fact]
    public void Shadow_SetsShadowOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Shadow, 4, 4, 0, 0, 0x00000080, Radius: 10)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("SetShadow", call.Method);
        Assert.Equal(4f, call.X);
        Assert.Equal(4f, call.Y);
        Assert.Equal(10f, call.W); // blurRadius mapped to W in FakeCanvas DrawCall
        Assert.Equal(0x00000080u, call.Color);
    }

    [Fact]
    public void Blur_SetsBlurOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Blur, 0, 0, 0, 0, 0, Radius: 20)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("SetBlur", call.Method);
        Assert.Equal(20f, call.X); // radius mapped to X in FakeCanvas
    }

    [Fact]
    public void LinearGrad_SetsGradientOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.LinearGrad, 0, 0, 100, 200, 0xFF0000FF, Color2: 0x00FF00FF)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("SetLinearGradient", call.Method);
        Assert.Equal(0f, call.X);
        Assert.Equal(0f, call.Y);
        Assert.Equal(100f, call.W);
        Assert.Equal(200f, call.H);
        Assert.Equal(0xFF0000FFu, call.Color);
    }

    [Fact]
    public void RadialGrad_SetsRadialGradientOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.RadialGrad, 50, 50, 0, 0, 0xFFAAAAFF, Radius: 80, Color2: 0x00000000)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("SetRadialGradient", call.Method);
        Assert.Equal(50f, call.X);
        Assert.Equal(50f, call.Y);
        Assert.Equal(80f, call.W); // radius mapped to W in FakeCanvas
        Assert.Equal(0xFFAAAAFFu, call.Color);
    }

    [Fact]
    public void Stroke_DrawsStrokeRectOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Stroke, 10, 20, 200, 100, 0xFF00FF00, Radius: 8, FontSize: 2)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawStrokeRect", call.Method);
        Assert.Equal(10f, call.X);
        Assert.Equal(20f, call.Y);
        Assert.Equal(200f, call.W);
        Assert.Equal(100f, call.H);
        Assert.Equal(8f, call.Radius);
        Assert.Equal(2f, call.FontSize); // strokeWidth reused from FontSize
        Assert.Equal(0xFF00FF00u, call.Color);
    }

    [Fact]
    public void Path_DrawsPathOnCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Path, 0, 0, 0, 0, 0xFF0000FF, Text: "M 10 10 L 50 50 Z")]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawPath", call.Method);
        Assert.Equal("M 10 10 L 50 50 Z", call.Text);
        Assert.Equal(0xFF0000FFu, call.Color);
    }

    [Fact]
    public void Path_NullText_DrawsEmptyPath()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Path, 0, 0, 0, 0, 0xFFFFFFFF, Text: null)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("DrawPath", call.Method);
        Assert.Equal("", call.Text);
    }

    [Fact]
    public void Scale_ScalesCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([new RenderCommand(RenderOp.Scale, 2.0f, 0.5f, 0, 0, 0)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("Scale", call.Method);
        Assert.Equal(2.0f, call.X);
        Assert.Equal(0.5f, call.Y);
    }

    [Fact]
    public void Rotate_RotatesCanvas()
    {
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        float angle = 1.5708f; // ~90 degrees in radians
        renderer.Render([new RenderCommand(RenderOp.Rotate, angle, 0, 0, 0, 0)]);

        var call = Assert.Single(canvas.Calls);
        Assert.Equal("Rotate", call.Method);
        Assert.Equal(angle, call.X);
    }

    [Fact]
    public void GlassFrame_ShadowThenBlurThenGradientThenShape()
    {
        // Simulates a typical Glass UI card: shadow + blur + gradient fill + rounded rect
        var canvas = new FakeCanvas();
        var renderer = new MabelRenderer(canvas);

        renderer.Render([
            new RenderCommand(RenderOp.BeginFrame, 0, 0, 0, 0, 0xFF000000),
            new RenderCommand(RenderOp.Shadow, 0, 4, 0, 0, 0x00000040, Radius: 16),
            new RenderCommand(RenderOp.Blur, 0, 0, 0, 0, 0, Radius: 30),
            new RenderCommand(RenderOp.LinearGrad, 0, 0, 0, 200, 0xFFFFFF40, Color2: 0xFFFFFF10),
            new RenderCommand(RenderOp.RoundRect, 20, 100, 335, 200, 0xFFFFFF20, Radius: 24),
            new RenderCommand(RenderOp.Stroke, 20, 100, 335, 200, 0xFFFFFF30, Radius: 24, FontSize: 0.5f),
            new RenderCommand(RenderOp.Text, 40, 140, 0, 0, 0xFFFFFFFF, Text: "Glass Card", FontSize: 28),
            new RenderCommand(RenderOp.EndFrame, 0, 0, 0, 0, 0),
        ]);

        // SaveState + Clear + SetShadow + SetBlur + SetLinearGradient + DrawRoundRect
        // + DrawStrokeRect + DrawText + RestoreState = 9 calls
        Assert.Equal(9, canvas.Calls.Count);
        Assert.Equal("SaveState", canvas.Calls[0].Method);
        Assert.Equal("Clear", canvas.Calls[1].Method);
        Assert.Equal("SetShadow", canvas.Calls[2].Method);
        Assert.Equal("SetBlur", canvas.Calls[3].Method);
        Assert.Equal("SetLinearGradient", canvas.Calls[4].Method);
        Assert.Equal("DrawRoundRect", canvas.Calls[5].Method);
        Assert.Equal("DrawStrokeRect", canvas.Calls[6].Method);
        Assert.Equal("DrawText", canvas.Calls[7].Method);
        Assert.Equal("RestoreState", canvas.Calls[8].Method);
    }
}
