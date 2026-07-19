namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Theming / dark-mode NO descritor (Onda 🟡).
//
// O guest declara TOKENS de design (cores, tipografia, espaçamento) em duas
// variantes — claro e escuro — e os nós referenciam tokens por NOME (Props.
// BackgroundToken/ColorToken/TextStyle/...) em vez de valores crus. Cada host
// escolhe a variante conforme o tema ativo do SO/usuário e resolve o token pro
// valor nativo. Trocar dark↔light NÃO exige novo descritor: o mesmo documento
// serve os dois.
//
// Platform-neutral: os tokens são só nomes+valores; o host aplica. Retrocompat:
// nós sem *Token continuam usando os valores crus (Background/Color/...).
// =============================================================================

/// <summary>Modo de tema. Byte-enum (decode UInt8 no host Swift).</summary>
public enum SduiThemeMode : byte
{
    /// Segue a preferência do SO/usuário (default).
    System = 0,
    /// Força tema claro.
    Light = 1,
    /// Força tema escuro.
    Dark = 2,
}

/// <summary>
/// Estilo de texto nomeado (token de tipografia): resolve fontSize+weight+cor de
/// uma vez. Todos os campos opcionais; o host preenche os ausentes com o default
/// do controle. A cor pode ser um token (ColorToken) ou crua (Color).
/// </summary>
public sealed record SduiTextStyle
{
    public float? FontSize { get; init; }
    public SduiFontWeight? Weight { get; init; }
    /// Cor crua RGBA (0xRRGGBBAA).
    public uint? Color { get; init; }
    /// Token de cor (resolvido na MESMA variante de tema). Vence Color.
    public string? ColorToken { get; init; }
}

/// <summary>
/// Uma VARIANTE de tema (a paleta clara OU a escura). Mapas token→valor. Nomes
/// livres (o guest define seu vocabulário: "surface", "primary", "onSurface"…).
/// </summary>
public sealed record SduiTheme
{
    /// token → cor RGBA (0xRRGGBBAA).
    public IReadOnlyDictionary<string, uint>? Colors { get; init; }
    /// token → estilo de texto (tipografia).
    public IReadOnlyDictionary<string, SduiTextStyle>? Text { get; init; }
    /// token → espaçamento (px lógicos).
    public IReadOnlyDictionary<string, float>? Spacing { get; init; }
}

/// <summary>Par de variantes claro/escuro. Ausência de uma variante ⇒ o host cai na outra.</summary>
public sealed record SduiThemeSet
{
    public SduiTheme? Light { get; init; }
    public SduiTheme? Dark { get; init; }
}

/// <summary>
/// Resolvedor de tokens de tema. Puro e testável: dado o conjunto de temas + o
/// modo ativo, resolve cores/estilos/espaçamentos por nome. Espelha a lógica que
/// cada host aplica nativamente.
/// </summary>
public sealed class SduiThemeResolver
{
    private readonly SduiTheme? _active;

    public SduiThemeResolver(SduiThemeSet? themes, SduiThemeMode mode, bool systemPrefersDark = false)
    {
        bool dark = mode switch
        {
            SduiThemeMode.Dark => true,
            SduiThemeMode.Light => false,
            _ => systemPrefersDark,
        };
        // Cai na outra variante quando a preferida não foi declarada.
        _active = dark ? (themes?.Dark ?? themes?.Light) : (themes?.Light ?? themes?.Dark);
    }

    /// A variante de tema efetivamente ativa (pode ser null se não há temas).
    public SduiTheme? Active => _active;

    /// <summary>Resolve um token de cor. null ⇒ token ausente (o host usa o valor cru).</summary>
    public uint? Color(string? token) =>
        token is not null && _active?.Colors is { } c && c.TryGetValue(token, out var v) ? v : null;

    /// <summary>Resolve um token de estilo de texto. null ⇒ ausente.</summary>
    public SduiTextStyle? TextStyle(string? token) =>
        token is not null && _active?.Text is { } t && t.TryGetValue(token, out var v) ? v : null;

    /// <summary>Resolve um token de espaçamento. null ⇒ ausente.</summary>
    public float? Spacing(string? token) =>
        token is not null && _active?.Spacing is { } s && s.TryGetValue(token, out var v) ? v : null;

    /// <summary>
    /// Cor efetiva de fundo de um nó: token do tema quando resolvido, senão o
    /// valor cru de Props.Background. Conveniência pros hosts.
    /// </summary>
    public uint? ResolveBackground(SduiProps? p) => Color(p?.BackgroundToken) ?? p?.Background;

    /// <summary>Cor de texto/tint efetiva: token, senão Props.Color.</summary>
    public uint? ResolveColor(SduiProps? p) => Color(p?.ColorToken) ?? p?.Color;

    /// <summary>Cor de borda efetiva: token, senão Props.BorderColor.</summary>
    public uint? ResolveBorderColor(SduiProps? p) => Color(p?.BorderColorToken) ?? p?.BorderColor;
}
