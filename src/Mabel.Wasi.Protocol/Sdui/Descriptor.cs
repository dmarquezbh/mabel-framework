namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// SDUI (Server/Guest-Driven UI) descriptor.
//
// Uma ÁRVORE SEMÂNTICA de UI — não um display-list de pixels. O guest (board_gen
// hoje; WASM amanhã) descreve CONTROLES ("uma lista de cards com um título e uma
// barra de progresso"); cada host de plataforma (Mabel.Host.Ios etc.) mapeia a
// árvore pra CONTROLES NATIVOS REAIS (UIScrollView/UICollectionView/UILabel/...).
//
// Vantagem sobre o display-list (RenderOp em Protocol.cs): scroll, hit-testing,
// acessibilidade (VoiceOver), seleção de texto, IME e Dynamic Type vêm DE GRAÇA
// do SO, porque são controles de verdade. O host fica "burro": só traduz nós.
//
// Versionável (SchemaVersion) e platform-agnostic. Transporte v1 = JSON
// (mesmo shape emitido pelo board_gen e lido pelo host). Um transporte binário
// WASI pode vir depois sem mudar o modelo.
// =============================================================================

/// <summary>Tipos de nó suportados na v1 (suficiente pra tela do Kanban Kanban).</summary>
public enum SduiNodeType : byte
{
    /// Raiz da tela. Um único filho (normalmente um ScrollView ou stack).
    Screen      = 0x01,
    /// Stack vertical (filhos empilhados de cima p/ baixo). → UIStackView(.vertical).
    VStack      = 0x02,
    /// Stack horizontal. → UIStackView(.horizontal).
    HStack      = 0x03,
    /// Container rolável. Props.Axis define a direção. → UIScrollView.
    ScrollView  = 0x04,
    /// Coleção de filhos homogêneos (rolável/reciclável). → UICollectionView.
    List        = 0x05,
    /// Container clicável com estilo de cartão. → UIControl (tap nativo).
    Card        = 0x06,
    /// Rótulo de texto. → UILabel.
    Text        = 0x07,
    /// Botão acionável. → UIButton.
    Button      = 0x08,
    /// Imagem ou ícone (Props.Src = asset id ou nome de SF Symbol). → UIImageView.
    Image       = 0x09,
    /// Pílula pequena com rótulo (chip/etiqueta). → UILabel estilizado.
    Badge       = 0x0A,
    /// Barra de progresso (Props.Value 0..1). → UIProgressView.
    ProgressBar = 0x0B,
    /// Separador fino. → UIView 1px.
    Divider     = 0x0C,
    /// Espaço flexível/fixo entre irmãos.
    Spacer      = 0x0D,
    /// Pilha de navegação: hospeda Screens e executa ações navigate:push/pop/…
    /// → UINavigationController. Ver Navigation.cs.
    NavStack    = 0x0E,

    // ── Onda 🟡 (funcional) — schema v3 ──────────────────────────────────────
    // Forms (ver Forms.cs), catálogo ampliado, media (ver Media.cs). Todos são
    // nós NOVOS: um host v2 não os reconhece e aplica SduiUnknownFallback
    // (default RenderChildren) — retrocompat preservada.

    /// Campo de texto editável. → UITextField / TextBox. Ver Forms.cs.
    TextField   = 0x0F,
    /// Seleção de uma opção dentre várias (dropdown/picker). → ComboBox. Ver Forms.cs.
    Select      = 0x10,
    /// Caixa de marcação booleana. → CheckBox. Ver Forms.cs.
    Checkbox    = 0x11,
    /// Alternância on/off. → ToggleSwitch. Ver Forms.cs.
    Switch      = 0x12,
    /// Controle deslizante contínuo (Props.Min/Max/Value/Step). → Slider. Ver Forms.cs.
    Slider      = 0x13,
    /// Incremento/decremento numérico (Props.Min/Max/Value/Step). → Stepper. Ver Forms.cs.
    Stepper     = 0x14,
    /// Barra de abas (Nav.Tabs). → UITabBarController / TabControl. Ver Navigation.cs.
    TabBar      = 0x15,
    /// Grade de N colunas (Props.Columns). → UICollectionView grid / UniformGrid.
    Grid        = 0x16,
    /// Painel modal/gaveta apresentado sobre o conteúdo. → sheet/modal nativo.
    Sheet       = 0x17,
    /// Imagem de perfil circular (Props.Src + iniciais em Props.Text). → ImageView redondo.
    Avatar      = 0x18,
    /// Pílula compacta com rótulo (e ícone/remoção opcionais). → chip nativo.
    Chip        = 0x19,
    /// Vídeo (Props.Src + Media). → AVPlayer / MediaElement. Ver Media.cs.
    Video       = 0x1A,
    /// Áudio (Props.Src + Media). → AVAudioPlayer / MediaElement. Ver Media.cs.
    Audio       = 0x1B,
}

/// <summary>Tipo de teclado sugerido a um TextField. Byte-enum (decode UInt8).</summary>
public enum SduiKeyboardType : byte
{
    Default = 0,
    /// Teclado numérico.
    Number = 1,
    /// Teclado de e-mail (@ e .).
    Email = 2,
    /// Teclado telefônico.
    Phone = 3,
    /// Teclado de URL.
    Url = 4,
    /// Entrada decimal (com separador).
    Decimal = 5,
}

public enum SduiAxis : byte { Vertical = 0, Horizontal = 1 }

/// <summary>Alinhamento no eixo cruzado de um stack.</summary>
public enum SduiAlign : byte { Start = 0, Center = 1, End = 2, Stretch = 3 }

public enum SduiFontWeight : byte { Regular = 0, Medium = 1, Semibold = 2, Bold = 3 }

/// <summary>Insets (top/right/bottom/left) em px lógicos.</summary>
public readonly record struct SduiEdges(float Top, float Right, float Bottom, float Left)
{
    /// Padding uniforme nos 4 lados.
    public static SduiEdges All(float v) => new(v, v, v, v);
}

/// <summary>
/// Ação semântica declarada por um nó (ex.: abrir card, filtrar). O host liga
/// o gesto/seleção NATIVO do controle a isto e devolve {Name, Args, node.Id}
/// pro app — sem coordenadas de pixel.
/// </summary>
public sealed record SduiAction(string Name, IReadOnlyDictionary<string, string>? Args = null)
{
    /// <summary>
    /// Navegação declarativa disparada por esta ação (opcional). Quando presente,
    /// o host manipula a pilha do NavStack (push/pop/replace/root/popTo) além de —
    /// ou em vez de — notificar o app. Ver Navigation.cs.
    /// </summary>
    public SduiNavigate? Navigate { get; init; }
}

/// <summary>
/// Propriedades de um nó. Bag achatado; só os campos relevantes ao Type são
/// setados (o resto fica null e é omitido no JSON). Cores = RGBA 0xRRGGBBAA
/// (mesmo formato do RenderCommand).
/// </summary>
public sealed record SduiProps
{
    // ── Layout ──────────────────────────────────────────────────────────────
    /// Espaçamento entre filhos de um stack/list.
    public float? Spacing { get; init; }
    public SduiEdges? Padding { get; init; }
    /// Alinhamento no eixo cruzado (stacks).
    public SduiAlign? Align { get; init; }
    public float? Width { get; init; }
    public float? Height { get; init; }
    /// Fator de crescimento (flex-grow). null/0 = tamanho do conteúdo.
    public float? Flex { get; init; }
    /// Direção de rolagem/lista (ScrollView, List).
    public SduiAxis? Axis { get; init; }

    // ── Estilo de caixa ───────────────────────────────────────────────────────
    public uint? Background { get; init; }
    public float? CornerRadius { get; init; }
    public uint? BorderColor { get; init; }
    public float? BorderWidth { get; init; }

    // ── Texto (Text, Button, Badge) ────────────────────────────────────────────
    public string? Text { get; init; }
    public float? FontSize { get; init; }
    public uint? Color { get; init; }
    public SduiFontWeight? Weight { get; init; }

    // ── Imagem/ícone ────────────────────────────────────────────────────────────
    /// Asset id do bundle OU nome de SF Symbol (ex.: "magnifyingglass").
    public string? Src { get; init; }

    // ── ProgressBar ─────────────────────────────────────────────────────────────
    /// Progresso 0..1.
    public float? Value { get; init; }

    // ── Layout responsivo / flexbox refinado ────────────────────────────────────
    /// Restrições de tamanho (px lógicos). Mantidas quando o nó é esticado/comprimido.
    public float? MinWidth { get; init; }
    public float? MaxWidth { get; init; }
    public float? MinHeight { get; init; }
    public float? MaxHeight { get; init; }
    /// Razão de aspecto largura/altura (ex.: 16f/9f). Preservada ao redimensionar.
    public float? AspectRatio { get; init; }
    /// flex-grow explícito. Sinônimo de Flex; se ambos setados, FlexGrow vence.
    public float? FlexGrow { get; init; }
    /// flex-shrink (0 = não encolhe). Default do host quando ausente = 1.
    public float? FlexShrink { get; init; }
    /// flex-basis: tamanho base no eixo principal antes de grow/shrink.
    public float? FlexBasis { get; init; }
    /// Quebra de linha dos filhos de um stack (flex-wrap). Ver SduiWrap.
    public SduiWrap? Wrap { get; init; }
    /// Bordas do safe-area (notch, home indicator, status bar) que este container
    /// respeita. Ver SduiSafeArea (flags). Relevante em Screen/containers de topo.
    public SduiSafeArea? SafeArea { get; init; }

    // ── Dados semânticos ─────────────────────────────────────────────────────────
    /// Metadados arbitrários do nó (campos do card: credor, código, valor...).
    /// Devolvidos ao app no tap, junto da ação. Não afetam layout/estilo.
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    // =========================================================================
    // Onda 🟡 (funcional) — schema v3. Todos os campos são OPCIONAIS: ausentes,
    // o host se comporta exatamente como na v2 (retrocompat).
    // =========================================================================

    // ── Theming: referências a TOKENS de tema (ver Theming.cs) ───────────────────
    // Quando um *Token está presente, o host resolve a cor/estilo pelo tema ativo
    // (claro/escuro) em vez do valor cru. O valor cru (Background/Color/...) vale
    // como literal/fallback quando o token não existe no tema.
    /// Token de cor de fundo (ex.: "surface", "card"). Ver SduiTheme.Colors.
    public string? BackgroundToken { get; init; }
    /// Token de cor de texto/tint (ex.: "onSurface", "primary").
    public string? ColorToken { get; init; }
    /// Token de cor de borda.
    public string? BorderColorToken { get; init; }
    /// Token de estilo de texto (ex.: "title", "body", "caption") — resolve
    /// fontSize+weight+cor de uma vez. Ver SduiTheme.Text.
    public string? TextStyle { get; init; }
    /// Token de espaçamento aplicado como Spacing quando presente (ex.: "sm","md").
    public string? SpacingToken { get; init; }

    // ── i18n / l10n (ver Localization.cs) ────────────────────────────────────────
    /// Chave de texto localizável. Presente ⇒ o host resolve pela tabela do locale
    /// ativo e interpola TextArgs; Text vira fallback quando a chave não existe.
    public string? TextKey { get; init; }
    /// Argumentos de interpolação da chave (placeholders {nome}).
    public IReadOnlyDictionary<string, string>? TextArgs { get; init; }

    // ── Forms / inputs (ver Forms.cs) ────────────────────────────────────────────
    /// Nome do campo no modelo do formulário. Liga o input a um valor de estado;
    /// devolvido ao app junto do valor digitado. Base do two-way binding.
    public string? Field { get; init; }
    /// Placeholder (texto-fantasma) de um input vazio.
    public string? Placeholder { get; init; }
    /// Placeholder localizável (i18n). Vence Placeholder quando resolvido.
    public string? PlaceholderKey { get; init; }
    /// Valor inicial (texto) de um input — TextField/Select/Slider/Stepper.
    /// Estado corrente é do host; isto é só a semente declarativa.
    public string? DefaultValue { get; init; }
    /// Estado marcado inicial de Checkbox/Switch.
    public bool? Checked { get; init; }
    /// TextField multilinha (textarea).
    public bool? Multiline { get; init; }
    /// TextField de senha (texto oculto).
    public bool? Secure { get; init; }
    /// Tipo de teclado sugerido (TextField).
    public SduiKeyboardType? Keyboard { get; init; }
    /// Opções de um Select. Ordem preservada.
    public IReadOnlyList<SduiOption>? Options { get; init; }
    /// Limite inferior (Slider/Stepper). Default do host = 0.
    public float? Min { get; init; }
    /// Limite superior (Slider/Stepper). Default do host = 1.
    public float? Max { get; init; }
    /// Passo de incremento (Slider/Stepper). Default do host = 1.
    public float? Step { get; init; }
    /// Estado desabilitado (não interativo) de um input/botão.
    public bool? Disabled { get; init; }

    // ── Catálogo ampliado ────────────────────────────────────────────────────────
    /// Número de colunas de um Grid. Default do host = 2.
    public int? Columns { get; init; }
    /// Sheet/modal atualmente apresentado (true) ou oculto (false). Ausente ⇒ false.
    public bool? Presented { get; init; }
}

/// <summary>
/// Nó da árvore SDUI. Imutável. `Id` é um identificador SEMÂNTICO estável
/// (ex.: "card:50231", "column:ORIGINACAO/Cadastro") — o host o devolve ao app
/// quando o controle nativo correspondente é tocado.
/// </summary>
public sealed record SduiNode
{
    public required string Id { get; init; }
    public required SduiNodeType Type { get; init; }
    public SduiProps? Props { get; init; }
    public IReadOnlyList<SduiNode>? Children { get; init; }
    /// Ação disparada no tap (opcional). Presença ⇒ o host torna o nó clicável.
    public SduiAction? OnTap { get; init; }

    // ── Semântica transversal (Onda 1: fundacionais do schema) ───────────────────

    /// Semântica de acessibilidade (VoiceOver/traits). O host a mapeia pra
    /// accessibilityLabel/traits/hint nativos. Ver Accessibility.cs.
    public SduiA11y? A11y { get; init; }

    /// Política de degradação graciosa quando o HOST não reconhece este nó —
    /// Type desconhecido, ou MinSchemaVersion maior que a versão do host. Ausente
    /// ⇒ o host aplica o default do contrato (RenderChildren). Ver Compatibility.cs.
    public SduiUnknownFallback? Fallback { get; init; }

    /// Versão mínima de schema que o host precisa entender pra renderizar este nó
    /// fielmente. Host mais antigo aplica Fallback. Ausente ⇒ 1.
    public int? MinSchemaVersion { get; init; }

    /// Variações de Props por classe de tamanho / breakpoint (responsivo). O host
    /// escolhe a primeira variação cujo When casa e a sobrepõe. Ver Responsive.cs.
    public IReadOnlyList<SduiResponsiveOverride>? Responsive { get; init; }

    /// Dados de lista virtualizada. Presente ⇒ este nó (Type=List) é lazy: o host
    /// recicla views a partir de ItemTemplate + Items, sem expandir N nós. Distingue
    /// List-virtualizada de VStack-estático. Ver Lists.cs.
    public SduiListData? List { get; init; }

    /// Metadados de navegação (rota nomeada, título) quando este nó é um Screen
    /// dentro de um NavStack. Ver Navigation.cs.
    public SduiNav? Nav { get; init; }

    /// Ligações template→dados: mapeia prop-alvo ("text", "value", "src", ou uma
    /// chave de Data) → chave em SduiListItem.Data. Só usado dentro do ItemTemplate
    /// de uma List virtualizada, onde o host substitui por linha.
    public IReadOnlyDictionary<string, string>? Bind { get; init; }

    // ── Semântica transversal (Onda 🟡: funcional) — schema v3 ────────────────────

    /// Animação/transição declarativa deste nó (fade/slide/scale/expand). O host
    /// a aplica ao aparecer/tocar/continuamente. Ver Animation.cs.
    public SduiAnimation? Animation { get; init; }

    /// Metadados de media (poster, autoplay, loop, controls) para Video/Audio.
    /// Ver Media.cs.
    public SduiMedia? Media { get; init; }

    /// Regras de validação declarativas de um input (Field). O host mostra o
    /// estado de erro; SduiValidator avalia contra os valores. Ver Forms.cs.
    public IReadOnlyList<SduiValidationRule>? Validation { get; init; }

    /// Abas de um nó TabBar (rota + rótulo + ícone por aba). Ver Navigation.cs.
    public IReadOnlyList<SduiTab>? Tabs { get; init; }

    /// Ação disparada quando este nó (tipicamente Screen) entra em tela.
    /// Lifecycle hook — o host a invoca no onAppear nativo.
    public SduiAction? OnAppear { get; init; }

    /// Ação disparada quando este nó sai de tela (onDisappear nativo).
    public SduiAction? OnDisappear { get; init; }
}

/// <summary>Envelope de topo do documento SDUI. É isto que trafega como JSON.</summary>
public sealed record SduiDocument
{
    /// Versão do schema — o host recusa/adapta se não reconhecer.
    public int SchemaVersion { get; init; } = 1;
    public required SduiNode Root { get; init; }

    // ── Onda 🟡 (funcional) — recursos de nível de documento (schema v3) ──────────

    /// Conjunto de temas (claro/escuro) + tokens de cor/tipografia/espaçamento.
    /// Os nós referenciam tokens via Props.*Token; o host resolve pelo tema ativo.
    /// Ausente ⇒ sem theming (nós usam valores crus). Ver Theming.cs.
    public SduiThemeSet? Themes { get; init; }

    /// Modo de tema padrão sugerido pelo guest (System/Light/Dark). O host pode
    /// sobrepor pela preferência do SO/usuário. Ausente ⇒ System.
    public SduiThemeMode? ThemeMode { get; init; }

    /// Tabela de strings localizáveis por locale. Os nós referenciam chaves via
    /// Props.TextKey; o host resolve pelo locale ativo. Ver Localization.cs.
    public SduiLocalization? Localization { get; init; }
}
