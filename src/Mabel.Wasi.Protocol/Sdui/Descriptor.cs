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
}

/// <summary>Envelope de topo do documento SDUI. É isto que trafega como JSON.</summary>
public sealed record SduiDocument
{
    /// Versão do schema — o host recusa/adapta se não reconhecer.
    public int SchemaVersion { get; init; } = 1;
    public required SduiNode Root { get; init; }
}
