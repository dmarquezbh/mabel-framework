package com.mabel.host.sdui

import org.json.JSONObject

// =============================================================================
// Mabel SDUI - modelo Android (Kotlin) - schema v2
// Espelha Mabel.Wasi.Protocol/Sdui/*.cs e MabelSdui.swift (host iOS, schema v2).
//
// Uma ARVORE SEMANTICA de UI, nao um display-list de pixels. O guest descreve
// CONTROLES; o host Android (MabelViewBuilder em SduiCompose.kt) mapeia a arvore
// pra CONTROLES NATIVOS Jetpack Compose reais. Scroll, hit-testing, a11y e text
// scaling vem DE GRACA do SO.
//
// Onda 2 (paridade com o host iOS): decode TOLERANTE (OTA-safe), Fallback de nos
// desconhecidos, navegacao (NavStack), listas com itemTemplate + binding, e
// props responsivas. Transporte v1 = JSON (mesmo kanban-sdui.json consumido pelo
// host iOS/Windows). Decodificado aqui com org.json (sem plugins extras).
// =============================================================================

/** Versao de schema que este host conhece. Nos com minSchemaVersion > isso caem no Fallback. */
const val HOST_SCHEMA_VERSION = 3

/**
 * Tipos de no suportados. Valores byte iguais em todas as plataformas.
 * Onda 2 adiciona NavStack(14). Valores desconhecidos NAO estao aqui — o decode
 * tolerante preserva o raw e o builder aplica Fallback (ex.: type 200 futuro).
 */
enum class SduiNodeType(val raw: Int) {
    Screen(1), VStack(2), HStack(3), ScrollView(4), List(5),
    Card(6), Text(7), Button(8), Image(9), Badge(10),
    ProgressBar(11), Divider(12), Spacer(13), NavStack(14), TextField(15);

    companion object {
        /** null (nao estoura) para valores desconhecidos — degradacao graciosa. */
        fun fromOrNull(raw: Int): SduiNodeType? = entries.firstOrNull { it.raw == raw }
    }
}

enum class SduiAxis(val raw: Int) { Vertical(0), Horizontal(1) }
enum class SduiAlign(val raw: Int) { Start(0), Center(1), End(2), Stretch(3) }
enum class SduiFontWeight(val raw: Int) { Regular(0), Medium(1), Semibold(2), Bold(3) }

/** Politica de Fallback pra nos nao suportados. */
enum class SduiFallback(val raw: Int) {
    RenderChildren(0), Placeholder(1), Ignore(2);
    companion object { fun from(raw: Int?): SduiFallback = entries.firstOrNull { it.raw == raw } ?: RenderChildren }
}

/** Tipo de navegacao declarativa. */
enum class SduiNavKind(val raw: Int) {
    Push(0), Pop(1), Replace(2), Root(3), PopTo(4);
    companion object { fun from(raw: Int?): SduiNavKind = entries.firstOrNull { it.raw == raw } ?: Push }
}

/** Insets (top/right/bottom/left) em px logicos. */
data class SduiEdges(val top: Float, val right: Float, val bottom: Float, val left: Float)

/** Navegacao declarativa embutida numa acao. */
data class SduiNavigate(val kind: SduiNavKind, val route: String? = null, val params: Map<String, String> = emptyMap())

/** Acao semantica declarada por um no (ex.: abrir card, navegar). */
data class SduiAction(val name: String, val args: Map<String, String> = emptyMap(), val navigate: SduiNavigate? = null)

/** Metadados de navegacao de um Screen (rota, titulo). */
data class SduiNav(val route: String? = null, val title: String? = null, val modal: Boolean = false, val hidesNavBar: Boolean = false)

/** Acessibilidade (mapeada a semantics do Compose no builder). */
data class SduiA11y(
    val label: String? = null, val role: Int? = null, val hint: String? = null,
    val hidden: Boolean? = null, val value: String? = null,
)

/**
 * Propriedades de um no. Bag achatado; so os campos relevantes ao Type sao
 * setados (o resto fica null). Cores = RGBA 0xRRGGBBAA (mesmo formato do host
 * iOS/Windows). Ha `mergedOver` pra responsivo (merge raso: o override vence
 * campo a campo).
 */
data class SduiProps(
    // Layout
    val spacing: Float? = null,
    val padding: SduiEdges? = null,
    val align: Int? = null,
    val width: Float? = null,
    val height: Float? = null,
    val flex: Float? = null,
    val axis: Int? = null,
    // Box
    val background: Long? = null,
    val cornerRadius: Float? = null,
    val borderColor: Long? = null,
    val borderWidth: Float? = null,
    // Text
    val text: String? = null,
    val fontSize: Float? = null,
    val color: Long? = null,
    val weight: Int? = null,
    // Misc
    val src: String? = null,
    val value: Float? = null,
    // Flex / responsivo refinado
    val minWidth: Float? = null,
    val maxWidth: Float? = null,
    val minHeight: Float? = null,
    val maxHeight: Float? = null,
    val flexGrow: Float? = null,
    val safeArea: Int? = null,
    val data: Map<String, String> = emptyMap(),
    // TextField
    val placeholder: String? = null,
) {
    /** Merge raso: campos setados em [o] vencem; os demais herdam de this. */
    fun mergedOver(o: SduiProps): SduiProps = SduiProps(
        spacing = o.spacing ?: spacing,
        padding = o.padding ?: padding,
        align = o.align ?: align,
        width = o.width ?: width,
        height = o.height ?: height,
        flex = o.flex ?: flex,
        axis = o.axis ?: axis,
        background = o.background ?: background,
        cornerRadius = o.cornerRadius ?: cornerRadius,
        borderColor = o.borderColor ?: borderColor,
        borderWidth = o.borderWidth ?: borderWidth,
        text = o.text ?: text,
        fontSize = o.fontSize ?: fontSize,
        color = o.color ?: color,
        weight = o.weight ?: weight,
        src = o.src ?: src,
        value = o.value ?: value,
        minWidth = o.minWidth ?: minWidth,
        maxWidth = o.maxWidth ?: maxWidth,
        minHeight = o.minHeight ?: minHeight,
        maxHeight = o.maxHeight ?: maxHeight,
        flexGrow = o.flexGrow ?: flexGrow,
        safeArea = o.safeArea ?: safeArea,
        data = if (o.data.isNotEmpty()) o.data else data,
        placeholder = o.placeholder ?: placeholder,
    )
}

/** Override responsivo: aplica-se quando a classe de largura casa. */
data class SduiResponsiveOverride(
    val widthClass: Int? = null,   // 0 any, 1 compact, 2 regular
    val heightClass: Int? = null,
    val minContainerWidth: Float? = null,
    val props: SduiProps,
)

/** Um item concreto de uma List (dados de binding + id semantico + tap proprio). */
data class SduiListItem(val id: String, val data: Map<String, String> = emptyMap(), val onTap: SduiAction? = null)

/** Dados de uma List: template + itens + virtualizacao. */
data class SduiListData(
    val itemTemplate: SduiNode,
    val items: List<SduiListItem> = emptyList(),
    val virtualized: Boolean = true,
    val axis: Int? = null,
    val estimatedItemExtent: Float? = null,
    val count: Int? = null,
)

/**
 * No da arvore SDUI. Imutavel. Decode TOLERANTE: [typeRaw] guarda o valor cru;
 * [type] mapeia pra enum ou null (schema futuro -> Fallback). `id` e um
 * identificador SEMANTICO estavel (ex.: "card:50000") — o host o devolve ao app
 * quando o controle nativo correspondente e tocado.
 */
data class SduiNode(
    val id: String,
    val typeRaw: Int,
    val props: SduiProps? = null,
    val children: List<SduiNode> = emptyList(),
    val onTap: SduiAction? = null,
    val a11y: SduiA11y? = null,
    val fallback: Int? = null,
    val minSchemaVersion: Int? = null,
    val responsive: List<SduiResponsiveOverride> = emptyList(),
    val list: SduiListData? = null,
    val nav: SduiNav? = null,
    val bind: Map<String, String> = emptyMap(),
    /** Acao de mudanca de texto (TextField). Ver Mabel.Wasi.Protocol/Sdui/Descriptor.cs. */
    val onChange: SduiAction? = null,
) {
    /** Tipo mapeado, ou null se o host nao conhece o valor. */
    val type: SduiNodeType? get() = SduiNodeType.fromOrNull(typeRaw)
}

/** Envelope de topo do documento SDUI. */
data class SduiDocument(val schemaVersion: Int, val root: SduiNode)

// =============================================================================
// Parser (org.json) — decode TOLERANTE, mesma responsabilidade do Decodable
// custom no Swift (type como raw, nunca estoura por valor desconhecido).
// =============================================================================

object SduiParser {
    fun parse(json: String): SduiDocument {
        val obj = JSONObject(json)
        return SduiDocument(
            schemaVersion = obj.optInt("schemaVersion", 1),
            root = node(obj.getJSONObject("root")),
        )
    }

    private fun node(o: JSONObject): SduiNode {
        val childrenArr = o.optJSONArray("children")
        val children = if (childrenArr != null) {
            (0 until childrenArr.length()).map { node(childrenArr.getJSONObject(it)) }
        } else emptyList()

        return SduiNode(
            id = o.getString("id"),
            typeRaw = o.getInt("type"),
            props = o.optJSONObject("props")?.let { props(it) },
            children = children,
            onTap = o.optJSONObject("onTap")?.let { action(it) },
            a11y = o.optJSONObject("a11y")?.let { a11y(it) },
            fallback = o.optIntOrNull("fallback"),
            minSchemaVersion = o.optIntOrNull("minSchemaVersion"),
            responsive = o.optJSONArray("responsive")?.let { arr ->
                (0 until arr.length()).map { responsive(arr.getJSONObject(it)) }
            } ?: emptyList(),
            list = o.optJSONObject("list")?.let { listData(it) },
            nav = o.optJSONObject("nav")?.let { nav(it) },
            bind = o.optJSONObject("bind")?.toStringMap() ?: emptyMap(),
            onChange = o.optJSONObject("onChange")?.let { action(it) },
        )
    }

    private fun props(o: JSONObject): SduiProps = SduiProps(
        spacing = o.optFloatOrNull("spacing"),
        padding = o.optJSONObject("padding")?.let { edges(it) },
        align = o.optIntOrNull("align"),
        width = o.optFloatOrNull("width"),
        height = o.optFloatOrNull("height"),
        flex = o.optFloatOrNull("flex"),
        axis = o.optIntOrNull("axis"),
        background = o.optLongOrNull("background"),
        cornerRadius = o.optFloatOrNull("cornerRadius"),
        borderColor = o.optLongOrNull("borderColor"),
        borderWidth = o.optFloatOrNull("borderWidth"),
        text = o.optStringOrNull("text"),
        fontSize = o.optFloatOrNull("fontSize"),
        color = o.optLongOrNull("color"),
        weight = o.optIntOrNull("weight"),
        src = o.optStringOrNull("src"),
        value = o.optFloatOrNull("value"),
        minWidth = o.optFloatOrNull("minWidth"),
        maxWidth = o.optFloatOrNull("maxWidth"),
        minHeight = o.optFloatOrNull("minHeight"),
        maxHeight = o.optFloatOrNull("maxHeight"),
        flexGrow = o.optFloatOrNull("flexGrow"),
        safeArea = o.optIntOrNull("safeArea"),
        data = o.optJSONObject("data")?.toStringMap() ?: emptyMap(),
        placeholder = o.optStringOrNull("placeholder"),
    )

    private fun edges(it: JSONObject) = SduiEdges(
        it.optDouble("top", 0.0).toFloat(),
        it.optDouble("right", 0.0).toFloat(),
        it.optDouble("bottom", 0.0).toFloat(),
        it.optDouble("left", 0.0).toFloat(),
    )

    private fun action(o: JSONObject): SduiAction = SduiAction(
        name = o.getString("name"),
        args = o.optJSONObject("args")?.toStringMap() ?: emptyMap(),
        navigate = o.optJSONObject("navigate")?.let {
            SduiNavigate(
                kind = SduiNavKind.from(it.optIntOrNull("kind")),
                route = it.optStringOrNull("route"),
                params = it.optJSONObject("params")?.toStringMap() ?: emptyMap(),
            )
        },
    )

    private fun nav(o: JSONObject) = SduiNav(
        route = o.optStringOrNull("route"),
        title = o.optStringOrNull("title"),
        modal = o.optBoolean("modal", false),
        hidesNavBar = o.optBoolean("hidesNavBar", false),
    )

    private fun a11y(o: JSONObject) = SduiA11y(
        label = o.optStringOrNull("label"),
        role = o.optIntOrNull("role"),
        hint = o.optStringOrNull("hint"),
        hidden = if (o.has("hidden")) o.optBoolean("hidden") else null,
        value = o.optStringOrNull("value"),
    )

    private fun responsive(o: JSONObject) = SduiResponsiveOverride(
        widthClass = o.optIntOrNull("widthClass"),
        heightClass = o.optIntOrNull("heightClass"),
        minContainerWidth = o.optFloatOrNull("minContainerWidth"),
        props = props(o.getJSONObject("props")),
    )

    private fun listData(o: JSONObject): SduiListData {
        val itemsArr = o.optJSONArray("items")
        val items = if (itemsArr != null) {
            (0 until itemsArr.length()).map {
                val io = itemsArr.getJSONObject(it)
                SduiListItem(
                    id = io.getString("id"),
                    data = io.optJSONObject("data")?.toStringMap() ?: emptyMap(),
                    onTap = io.optJSONObject("onTap")?.let { a -> action(a) },
                )
            }
        } else emptyList()
        return SduiListData(
            itemTemplate = node(o.getJSONObject("itemTemplate")),
            items = items,
            virtualized = o.optBoolean("virtualized", true),
            axis = o.optIntOrNull("axis"),
            estimatedItemExtent = o.optFloatOrNull("estimatedItemExtent"),
            count = o.optIntOrNull("count"),
        )
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private fun JSONObject.optFloatOrNull(k: String): Float? =
        if (has(k) && !isNull(k)) getDouble(k).toFloat() else null

    private fun JSONObject.optIntOrNull(k: String): Int? =
        if (has(k) && !isNull(k)) getInt(k) else null

    private fun JSONObject.optLongOrNull(k: String): Long? =
        if (has(k) && !isNull(k)) getLong(k) else null

    private fun JSONObject.optStringOrNull(k: String): String? =
        if (has(k) && !isNull(k)) getString(k) else null

    private fun JSONObject.toStringMap(): Map<String, String> =
        keys().asSequence().associateWith { getString(it) }
}
