namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Media (Onda 🟡).
//
// Image já existia (Props.Src). Esta camada adiciona semântica de MEDIA temporal
// (Video/Audio) e refinamentos de imagem. O nó carrega Props.Src (asset/URL) e
// SduiNode.Media com o comportamento de reprodução. O host mapeia pro player
// nativo (AVPlayer/MediaElement/ExoPlayer).
// =============================================================================

/// <summary>Como uma imagem/vídeo preenche sua caixa. Byte-enum (decode UInt8).</summary>
public enum SduiContentFit : byte
{
    /// Preenche mantendo a razão de aspecto, cortando o excesso (cover).
    Cover = 0,
    /// Cabe inteiro mantendo a razão de aspecto, com folga (contain).
    Contain = 1,
    /// Estica pra preencher, sem manter razão (fill).
    Fill = 2,
}

/// <summary>
/// Metadados de media de um nó Video/Audio (e refinamentos de Image/ContentFit).
/// Todos opcionais; ausência ⇒ default do player nativo.
/// </summary>
public sealed record SduiMedia
{
    /// Inicia a reprodução automaticamente ao aparecer.
    public bool? Autoplay { get; init; }
    /// Repete em loop ao terminar.
    public bool? Loop { get; init; }
    /// Inicia sem áudio.
    public bool? Muted { get; init; }
    /// Exibe os controles de transporte nativos (play/pause/scrub).
    public bool? Controls { get; init; }
    /// Imagem/poster exibida antes do play (asset id/URL, ou token via PosterToken).
    public string? Poster { get; init; }
    /// Como o conteúdo preenche a caixa.
    public SduiContentFit? Fit { get; init; }
}
