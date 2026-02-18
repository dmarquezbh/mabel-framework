namespace Mabel.Wasi.Protocol;

/// <summary>
/// Nomes das funcoes WASI import/export que formam o contrato entre Guest e Host.
/// 
/// Guest (WASM) exporta:
///   mabel_init()              -> chamado uma vez na inicializacao
///   mabel_update(event_ptr)   -> chamado a cada input event
///   mabel_render(buf_ptr)     -> retorna render commands no buffer
///
/// Host importa para o Guest:
///   mabel_draw_rect(x, y, w, h, color)
///   mabel_draw_text(x, y, text_ptr, text_len, color, font_size)
///   mabel_draw_circle(cx, cy, r, color)
///   mabel_measure_text(text_ptr, text_len, font_size) -> width
///   mabel_log(msg_ptr, msg_len)
/// </summary>
public static class WasiContract
{
    // Guest exports
    public const string Init   = "mabel_init";
    public const string Update = "mabel_update";
    public const string Render = "mabel_render";

    // Host imports (module name)
    public const string HostModule = "mabel";

    // Host import functions — Primitives
    public const string DrawRect    = "draw_rect";
    public const string DrawText    = "draw_text";
    public const string DrawCircle  = "draw_circle";
    public const string DrawLine    = "draw_line";
    public const string DrawImage   = "draw_image";
    public const string MeasureText = "measure_text";

    // Host import functions — Effects (Glass / modern UI)
    public const string SetShadow       = "set_shadow";
    public const string ClearShadow     = "clear_shadow";
    public const string SetBlur         = "set_blur";
    public const string ClearBlur       = "clear_blur";
    public const string SetLinearGrad   = "set_linear_gradient";
    public const string SetRadialGrad   = "set_radial_gradient";
    public const string ClearGradient   = "clear_gradient";
    public const string DrawStrokeRect  = "draw_stroke_rect";
    public const string DrawPath        = "draw_path";

    // Host import functions — State
    public const string PushClip    = "push_clip";
    public const string PopClip     = "pop_clip";
    public const string PushOpacity = "push_opacity";
    public const string PopOpacity  = "pop_opacity";
    public const string TranslateOp = "translate";
    public const string ScaleOp     = "scale";
    public const string RotateOp    = "rotate";

    // Host import functions — Utility
    public const string Log = "log";
}
