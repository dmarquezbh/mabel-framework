namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Acessibilidade NO descritor.
//
// "A11y de graça do nativo" só acontece se o descritor CARREGAR a semântica: o
// host mapeia estes campos pra accessibilityLabel / accessibilityTraits /
// accessibilityHint / accessibilityElementsHidden do UIKit (e equivalentes em
// outras plataformas). Sem isso, o VoiceOver lê o texto cru ou nada.
//
// Platform-neutral: os papéis/traits são semânticos (não "UIAccessibilityTrait"),
// e cada host traduz. Byte-enums pra bater com o decode UInt8 do host Swift.
// =============================================================================

/// <summary>
/// Papel semântico do nó pra tecnologias assistivas. O host mapeia pro trait
/// nativo correspondente (ex.: Button → .button, Header → .header no iOS).
/// </summary>
public enum SduiA11yRole : byte
{
    /// Sem papel específico; herda do controle nativo subjacente.
    None = 0,
    /// Elemento acionável (UIAccessibilityTraits.button).
    Button = 1,
    /// Cabeçalho de seção — permite navegação por títulos (.header).
    Header = 2,
    /// Link/navegação (.link).
    Link = 3,
    /// Imagem (.image).
    Image = 4,
    /// Texto estático (.staticText).
    Text = 5,
    /// Valor ajustável por gesto de incremento/decremento (.adjustable).
    Adjustable = 6,
    /// Campo/elemento de busca (.searchField).
    Search = 7,
    /// Elemento-resumo lido primeiro numa tela (.summaryElement).
    Summary = 8,
    /// Alternância on/off (host pode expor como switch).
    Toggle = 9,
    /// Indicador de progresso (host anuncia o Value).
    ProgressIndicator = 10,
}

/// <summary>
/// Traits adicionais combináveis (flags). Complementam o Role. O host faz OR
/// dos traits nativos correspondentes.
/// </summary>
[Flags]
public enum SduiA11yTraits : uint
{
    None = 0,
    /// Atualmente selecionado (.selected).
    Selected = 1 << 0,
    /// Desabilitado/não interativo (.notEnabled).
    Disabled = 1 << 1,
    /// Valor muda com frequência; o host pode reanunciar (.updatesFrequently).
    UpdatesFrequently = 1 << 2,
    /// Reproduz som ao ser ativado (.playsSound).
    PlaysSound = 1 << 3,
    /// Inicia uma sessão de mídia ao ser ativado (.startsMediaSession).
    StartsMediaSession = 1 << 4,
    /// Faz parte de conteúdo que causa update na tela (.causesPageTurn).
    CausesPageTurn = 1 << 5,
}

/// <summary>
/// Semântica de acessibilidade de um nó. Todos os campos são opcionais; ausência
/// ⇒ o host usa o default do controle nativo (ex.: UILabel já expõe seu texto).
/// </summary>
public sealed record SduiA11y
{
    /// Rótulo lido pelo leitor de tela. Sobrepõe o texto derivado do controle.
    public string? Label { get; init; }

    /// Papel semântico. Ver SduiA11yRole.
    public SduiA11yRole? Role { get; init; }

    /// Dica de resultado da ação (ex.: "Abre o detalhe do card").
    public string? Hint { get; init; }

    /// Oculta o nó (e a subárvore) das tecnologias assistivas — pra elementos
    /// puramente decorativos. Mapeia pra accessibilityElementsHidden.
    public bool? Hidden { get; init; }

    /// Valor corrente anunciado (ex.: "72 por cento" pra uma ProgressBar).
    public string? Value { get; init; }

    /// Traits combináveis. Ver SduiA11yTraits.
    public SduiA11yTraits? Traits { get; init; }
}
