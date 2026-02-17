using Mabel.Wasi.Protocol;

namespace Mabel.Renderer;

/// <summary>
/// Interpreta uma lista de RenderCommands e desenha no ICanvas.
/// Este eh o unico renderer - funciona igual em todas as plataformas.
/// </summary>
public sealed class MabelRenderer
{
    private readonly ICanvas _canvas;

    public MabelRenderer(ICanvas canvas) => _canvas = canvas;

    public void Render(ReadOnlySpan<RenderCommand> commands)
    {
        foreach (ref readonly var cmd in commands)
        {
            switch (cmd.Op)
            {
                case RenderOp.BeginFrame:
                    // Save state so that any Translate calls during the frame
                    // don't leak into subsequent frames.
                    _canvas.SaveState();
                    _canvas.Clear(cmd.Color);
                    break;

                case RenderOp.Rect:
                    _canvas.DrawRect(cmd.X, cmd.Y, cmd.W, cmd.H, cmd.Color);
                    break;

                case RenderOp.RoundRect:
                    _canvas.DrawRoundRect(cmd.X, cmd.Y, cmd.W, cmd.H, cmd.Radius, cmd.Color);
                    break;

                case RenderOp.Circle:
                    _canvas.DrawCircle(cmd.X, cmd.Y, cmd.Radius, cmd.Color);
                    break;

                case RenderOp.Line:
                    _canvas.DrawLine(cmd.X, cmd.Y, cmd.W, cmd.H, cmd.Color);
                    break;

                case RenderOp.Text:
                    _canvas.DrawText(cmd.Text ?? "", cmd.X, cmd.Y, cmd.FontSize, cmd.Color);
                    break;

                case RenderOp.Image:
                    _canvas.DrawImage(cmd.Text ?? "", cmd.X, cmd.Y, cmd.W, cmd.H);
                    break;

                case RenderOp.PushClip:
                    _canvas.PushClip(cmd.X, cmd.Y, cmd.W, cmd.H);
                    break;

                case RenderOp.PopClip:
                    _canvas.PopClip();
                    break;

                case RenderOp.PushOpacity:
                    _canvas.PushOpacity(cmd.X); // X reused as opacity value
                    break;

                case RenderOp.PopOpacity:
                    _canvas.PopOpacity();
                    break;

                case RenderOp.Translate:
                    _canvas.Translate(cmd.X, cmd.Y);
                    break;

                case RenderOp.EndFrame:
                    // Restore the state saved at BeginFrame, undoing all
                    // Translate/clip/opacity changes from this frame.
                    _canvas.RestoreState();
                    break;

                default:
                    // Unknown op — skip silently. This allows forward compatibility
                    // when new ops are added to the protocol.
                    break;
            }
        }
    }
}
