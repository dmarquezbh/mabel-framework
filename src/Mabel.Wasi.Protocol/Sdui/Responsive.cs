namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Layout responsivo / adaptativo.
//
// UM descritor precisa servir telas de tamanhos diferentes (iPhone compact,
// iPad regular, split-view, landscape). Duas alavancas:
//
//   1. Refinamentos de layout diretos em SduiProps (min/max, aspect, flex
//      grow/shrink/basis, wrap, safeArea) — ver Descriptor.cs.
//   2. SduiResponsiveOverride: variações de Props condicionadas à classe de
//      tamanho / breakpoint. O host escolhe a PRIMEIRA regra que casa e faz um
//      merge raso dela sobre os Props base do nó.
//
// Classes de tamanho espelham o modelo de size classes do UIKit
// (horizontal/vertical compact vs regular), platform-neutral.
// =============================================================================

/// <summary>
/// Classe de tamanho de um eixo. Espelha UIUserInterfaceSizeClass.
/// Any = não restringe (curinga).
/// </summary>
public enum SduiSizeClass : byte
{
    Any = 0,
    /// Espaço apertado (ex.: largura de iPhone em portrait).
    Compact = 1,
    /// Espaço amplo (ex.: iPad, largura de iPhone Max em landscape).
    Regular = 2,
}

/// <summary>Quebra de linha dos filhos de um stack (flex-wrap).</summary>
public enum SduiWrap : byte
{
    /// Filhos numa única linha; podem transbordar/comprimir.
    NoWrap = 0,
    /// Filhos quebram pra próxima linha quando não cabem.
    Wrap = 1,
}

/// <summary>
/// Bordas do safe-area que um container respeita (flags). Combináveis.
/// </summary>
[Flags]
public enum SduiSafeArea : byte
{
    None = 0,
    Top = 1 << 0,
    Right = 1 << 1,
    Bottom = 1 << 2,
    Left = 1 << 3,
    All = Top | Right | Bottom | Left,
}

/// <summary>
/// Uma variação condicional de Props. O host aplica quando TODAS as condições
/// presentes casam (WidthClass, HeightClass, MinContainerWidth). Regras são
/// avaliadas na ordem da lista; a primeira que casa vence (mais específica antes).
/// </summary>
public sealed record SduiResponsiveOverride
{
    /// Classe de tamanho horizontal exigida (Any/ausente = não filtra).
    public SduiSizeClass? WidthClass { get; init; }

    /// Classe de tamanho vertical exigida (Any/ausente = não filtra).
    public SduiSizeClass? HeightClass { get; init; }

    /// Breakpoint: largura mínima do container (px lógicos) pra esta regra valer.
    public float? MinContainerWidth { get; init; }

    /// Props a sobrepor quando a regra casa. Merge RASO sobre os Props base do nó
    /// (campos setados aqui vencem; os demais herdam da base).
    public required SduiProps Props { get; init; }
}
