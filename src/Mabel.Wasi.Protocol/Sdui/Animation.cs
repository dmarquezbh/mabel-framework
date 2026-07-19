namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Animações / transições declarativas (Onda 🟡).
//
// O guest declara a animação de um nó (fade/slide/scale/expand) + o GATILHO
// (aparecer / tocar / contínuo) e a temporização; o host a executa com o
// animador nativo (UIView.animate / Storyboard WPF / Animatable Compose). Sem
// timeline imperativa no descritor — só intenção.
//
// Transições de navegação: SduiNavigate.Transition escolhe como a nova tela
// entra na pilha (ver Navigation.cs).
// =============================================================================

/// <summary>Efeito de animação de um nó. Byte-enum (decode UInt8 no host Swift).</summary>
public enum SduiAnimationKind : byte
{
    None = 0,
    /// Aparece/desaparece variando a opacidade.
    Fade = 1,
    /// Desliza a partir de uma borda (SduiAnimation.Direction).
    Slide = 2,
    /// Escala de um fator inicial até 1 (zoom).
    Scale = 3,
    /// Expande/colapsa a altura (accordion/disclosure).
    Expand = 4,
}

/// <summary>Quando a animação dispara. Byte-enum.</summary>
public enum SduiAnimationTrigger : byte
{
    /// Ao o nó entrar em tela (default).
    OnAppear = 0,
    /// Ao o nó ser tocado.
    OnTap = 1,
    /// Repetidamente (ex.: pulsar/loading).
    Continuous = 2,
}

/// <summary>Curva de temporização. Byte-enum.</summary>
public enum SduiEasing : byte
{
    Linear = 0,
    EaseIn = 1,
    EaseOut = 2,
    EaseInOut = 3,
    /// Mola (overshoot). Host mapeia pro spring nativo.
    Spring = 4,
}

/// <summary>Direção de um Slide. Byte-enum.</summary>
public enum SduiSlideFrom : byte
{
    Bottom = 0,
    Top = 1,
    Leading = 2,
    Trailing = 3,
}

/// <summary>
/// Animação declarativa de um nó. Todos os campos exceto Kind são opcionais; o
/// host aplica defaults sensatos (ex.: 250ms, EaseInOut).
/// </summary>
public sealed record SduiAnimation
{
    public required SduiAnimationKind Kind { get; init; }
    /// Quando dispara. Ausente ⇒ OnAppear.
    public SduiAnimationTrigger? Trigger { get; init; }
    /// Duração em ms.
    public int? DurationMs { get; init; }
    /// Atraso antes de iniciar, em ms.
    public int? DelayMs { get; init; }
    /// Curva de temporização.
    public SduiEasing? Easing { get; init; }
    /// Borda de origem de um Slide.
    public SduiSlideFrom? Direction { get; init; }
}
