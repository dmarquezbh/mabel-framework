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

    // Host import functions
    public const string DrawRect    = "draw_rect";
    public const string DrawText    = "draw_text";
    public const string DrawCircle  = "draw_circle";
    public const string MeasureText = "measure_text";
    public const string Log         = "log";
}
