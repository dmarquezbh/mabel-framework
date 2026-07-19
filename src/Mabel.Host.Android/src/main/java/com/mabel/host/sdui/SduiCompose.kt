package com.mabel.host.sdui

import android.util.Log
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// =============================================================================
// Mabel SDUI - MabelViewBuilder (Android / Jetpack Compose) - schema v2
//
// Irmao do MabelViewBuilder iOS (MabelSdui.swift, schema v2). Percorre A MESMA
// arvore SduiDocument (mesmo kanban-sdui.json de Onda 2) e instancia CONTROLES
// NATIVOS Compose: NavStack->pilha de telas, Screen->Box, VStack->Column,
// HStack->Row, List(itemTemplate+items)->LazyColumn/LazyRow com binding por
// linha, Card->controle clicavel, Text/Badge/Button->Text, ProgressBar->
// LinearProgressIndicator. Decode TOLERANTE: no de tipo desconhecido (ex.: 200)
// vira Fallback (placeholder / children / ignore) em vez de estourar.
//
// O host fica "burro": so traduz nos. Tap resolve o node.id semantico + dados de
// negocio e (se houver) executa navegacao declarativa. Contrato identico ao iOS.
// =============================================================================

const val SDUI_TAG = "MabelSDUI"

/**
 * Controlador de navegacao (NavStack). Mantem uma pilha OBSERVAVEL de Screens;
 * `current` recompoe quando a pilha muda (push/pop). Espelha o
 * UINavigationController do host iOS.
 */
class SduiNavController(root: SduiNode?, private val routes: Map<String, SduiNode>) {
    val stack = mutableStateListOf<SduiNode>().apply { if (root != null) add(root) }
    val current: SduiNode? get() = stack.lastOrNull()

    fun navigate(n: SduiNavigate) {
        Log.i(SDUI_TAG, "navigate kind=${n.kind} route=${n.route} depth=${stack.size}")
        when (n.kind) {
            SduiNavKind.Push -> routes[n.route]?.let { stack.add(it) }
            SduiNavKind.Pop -> if (stack.size > 1) stack.removeAt(stack.size - 1)
            SduiNavKind.Replace -> routes[n.route]?.let { if (stack.isNotEmpty()) stack[stack.size - 1] = it }
            SduiNavKind.Root -> {
                val first = stack.firstOrNull()
                stack.clear()
                (routes[n.route] ?: first)?.let { stack.add(it) }
            }
            SduiNavKind.PopTo -> {
                val idx = stack.indexOfFirst { it.nav?.route == n.route }
                if (idx >= 0) while (stack.size > idx + 1) stack.removeAt(stack.size - 1)
            }
        }
    }
}

/**
 * Ponto de entrada: renderiza um SduiDocument como arvore de controles nativos.
 *
 * @param onAction chamado no tap de um no com onTap (apos executar navegacao
 *   declarativa, se houver). Recebe (acao, no) — o app liga isso a estado/log.
 */
@Composable
fun MabelSduiRoot(
    doc: SduiDocument,
    modifier: Modifier = Modifier,
    onAction: (SduiAction, SduiNode) -> Unit = { action, node ->
        Log.i(SDUI_TAG, "tap resolved -> node.id='${node.id}' action='${action.name}' args=${action.args} data=${node.props?.data}")
    },
) {
    Box(modifier.fillMaxSize()) {
        RenderNode(doc.root, onAction, null, Modifier.fillMaxSize())
    }
}

/**
 * Renderiza um no e seus filhos. `binding` = contexto de dados da linha atual
 * (List virtualizada) usado pra resolver Bind. `outerModifier` carrega
 * size/weight/fill decididos pelo pai.
 */
@Composable
private fun RenderNode(
    node: SduiNode,
    onAction: (SduiAction, SduiNode) -> Unit,
    binding: Map<String, String>?,
    outerModifier: Modifier,
) {
    // 1) Degradacao graciosa (OTA): tipo desconhecido ou schema mais novo.
    val unsupported = node.type == null || (node.minSchemaVersion ?: 0) > HOST_SCHEMA_VERSION
    if (unsupported) {
        RenderFallback(node, onAction, binding, outerModifier)
        return
    }

    // 2) Props resolvidas (responsivo: 1a variacao que casa, merge raso).
    val p = resolveProps(node)

    // box-modifier: fundo, canto, borda, tamanho fixo (aplicados a todos)
    var m = outerModifier
    if (p?.width != null) m = m.width(p.width.dp)
    if (p?.height != null) m = m.height(p.height.dp)
    m = m.applyBox(p)
    m = m.applyA11y(node.a11y)

    // 3) List com itemTemplate tem precedencia sobre children estaticos.
    if (node.type == SduiNodeType.List && node.list != null) {
        RenderList(node, node.list, p, onAction, m); return
    }

    when (node.type!!) {
        SduiNodeType.NavStack -> RenderNavStack(node, onAction, m)

        SduiNodeType.Screen -> {
            var sm = m.fillMaxSize()
            if ((p?.safeArea ?: 0) != 0) sm = sm.safeDrawingPadding()
            Box(sm) {
                node.children.forEach { RenderNode(it, onAction, binding, Modifier.fillMaxSize()) }
            }
        }

        SduiNodeType.ScrollView -> {
            val horizontal = p?.axis == SduiAxis.Horizontal.raw
            val sm = if (horizontal) {
                m.horizontalScroll(rememberScrollState()).verticalScroll(rememberScrollState())
            } else {
                m.verticalScroll(rememberScrollState())
            }
            Box(sm.paddingOf(p)) {
                node.children.forEach { RenderNode(it, onAction, binding, Modifier) }
            }
        }

        SduiNodeType.VStack -> RenderColumn(node, onAction, binding, m)
        SduiNodeType.List -> // List sem itemTemplate = stack simples
            if (p?.axis == SduiAxis.Horizontal.raw) RenderRow(node, onAction, binding, m)
            else RenderColumn(node, onAction, binding, m)
        SduiNodeType.HStack -> RenderRow(node, onAction, binding, m)

        SduiNodeType.Card -> {
            val tap = node.onTap
            val cm = if (tap != null) m.clickable {
                Log.i(SDUI_TAG, "CARD TAP id='${node.id}'")
                onAction(tap, node)
            } else m
            Column(cm.paddingOf(p), verticalArrangement = spacing(p)) {
                node.children.forEach { RenderChild(it, onAction, binding, this) }
            }
        }

        SduiNodeType.Text -> Text(
            text = boundText(node, p, binding) ?: "",
            color = colorOf(p?.color) ?: Color.Unspecified,
            fontSize = (p?.fontSize ?: 14f).sp,
            fontWeight = fontWeightOf(p?.weight),
            maxLines = 1,
            modifier = m,
        )

        SduiNodeType.Badge -> Text(
            text = boundText(node, p, binding) ?: "",
            color = colorOf(p?.color) ?: Color.Unspecified,
            fontSize = (p?.fontSize ?: 10f).sp,
            fontWeight = fontWeightOf(p?.weight),
            maxLines = 1,
            modifier = m.padding(horizontal = 6.dp, vertical = 2.dp),
        )

        SduiNodeType.Button -> Text(
            text = boundText(node, p, binding) ?: "",
            color = colorOf(p?.color) ?: Color.Unspecified,
            fontSize = (p?.fontSize ?: 14f).sp,
            fontWeight = FontWeight.Medium,
            modifier = if (node.onTap != null) m.clickable { onAction(node.onTap, node) } else m,
        )

        SduiNodeType.ProgressBar -> LinearProgressIndicator(
            progress = { (p?.value ?: 0f).coerceIn(0f, 1f) },
            color = colorOf(p?.color) ?: Color(0xFF3B82F6),
            modifier = m.height(4.dp),
        )

        SduiNodeType.Divider -> Box(
            (if (p?.height != null) m else m.height(1.dp)).fillMaxWidth()
                .background(colorOf(p?.background) ?: Color(0xFFE5E5E5))
        )

        SduiNodeType.Spacer -> Spacer(m)

        SduiNodeType.Image -> Box(
            m.size((p?.width ?: 24f).dp, (p?.height ?: 24f).dp)
                .background(colorOf(p?.background) ?: Color(0x11000000))
        )
    }
}

// ── NavStack (pilha de telas nativa) ─────────────────────────────────────────

@Composable
private fun RenderNavStack(node: SduiNode, onAction: (SduiAction, SduiNode) -> Unit, m: Modifier) {
    val screens = node.children.filter { it.type == SduiNodeType.Screen }
    val routes = remember(node) { screens.mapNotNull { s -> s.nav?.route?.let { it to s } }.toMap() }
    val nav = remember(node) { SduiNavController(screens.firstOrNull(), routes) }

    // Handler encadeado: executa navegacao declarativa e ENTAO encaminha ao app.
    val wrapped: (SduiAction, SduiNode) -> Unit = { action, n ->
        action.navigate?.let { nav.navigate(it) }
        onAction(action, n)
    }

    Box(m.fillMaxSize()) {
        nav.current?.let { RenderNode(it, wrapped, null, Modifier.fillMaxSize()) }
    }
}

// ── List virtualizada (itemTemplate + items + binding por linha) ──────────────

@Composable
private fun RenderList(
    node: SduiNode,
    data: SduiListData,
    p: SduiProps?,
    onAction: (SduiAction, SduiNode) -> Unit,
    m: Modifier,
) {
    Log.i(SDUI_TAG, "list ${node.id}: items=${data.items.size} virtualized=${data.virtualized} axis=${data.axis}")
    val horizontal = (data.axis ?: p?.axis) == SduiAxis.Horizontal.raw
    val tpl = data.itemTemplate

    // Cada linha CLONA o template injetando: id semantico do item, dados de
    // negocio em props.data (devolvidos ao app no tap) e o onTap do item ou do
    // template. O binding (data) resolve os textos Bind dos filhos.
    val rowNode: (SduiListItem) -> SduiNode = { item ->
        tpl.copy(
            id = item.id,
            onTap = item.onTap ?: tpl.onTap,
            props = (tpl.props ?: SduiProps()).copy(data = item.data),
        )
    }

    if (horizontal) {
        LazyRow(m, horizontalArrangement = spacingH(p)) {
            items(data.items, key = { it.id }) { item ->
                RenderNode(rowNode(item), onAction, item.data, Modifier)
            }
        }
    } else {
        LazyColumn(m, verticalArrangement = spacing(p)) {
            items(data.items, key = { it.id }) { item ->
                RenderNode(rowNode(item), onAction, item.data, Modifier.fillMaxWidth())
            }
        }
    }
}

// ── Fallback (degradacao graciosa p/ nos nao suportados) ──────────────────────

@Composable
private fun RenderFallback(
    node: SduiNode,
    onAction: (SduiAction, SduiNode) -> Unit,
    binding: Map<String, String>?,
    m: Modifier,
) {
    Log.i(SDUI_TAG, "fallback node=${node.id} typeRaw=${node.typeRaw} policy=${node.fallback}")
    when (SduiFallback.from(node.fallback)) {
        SduiFallback.Ignore -> Spacer(Modifier.size(0.dp)) // nao ocupa espaco
        SduiFallback.Placeholder -> Text(
            text = "⚠ nó não suportado (${node.typeRaw})",
            color = Color(0xFF6D5A00),
            fontSize = 11.sp,
            maxLines = 1,
            modifier = m.background(Color(0x33FFEB3B)).padding(horizontal = 6.dp, vertical = 2.dp),
        )
        SduiFallback.RenderChildren -> Column(m, verticalArrangement = spacing(node.props)) {
            node.children.forEach { RenderNode(it, onAction, binding, Modifier) }
        }
    }
}

// ── Stacks nativos ────────────────────────────────────────────────────────

@Composable
private fun RenderColumn(node: SduiNode, onAction: (SduiAction, SduiNode) -> Unit, binding: Map<String, String>?, m: Modifier) {
    val p = node.props
    Column(
        modifier = m.paddingOf(p),
        verticalArrangement = spacing(p),
        horizontalAlignment = when (p?.align) {
            SduiAlign.Center.raw -> Alignment.CenterHorizontally
            SduiAlign.End.raw -> Alignment.End
            else -> Alignment.Start
        },
    ) {
        node.children.forEach { RenderChild(it, onAction, binding, this) }
    }
}

@Composable
private fun RenderRow(node: SduiNode, onAction: (SduiAction, SduiNode) -> Unit, binding: Map<String, String>?, m: Modifier) {
    val p = node.props
    Row(
        modifier = m.paddingOf(p),
        horizontalArrangement = spacingH(p),
        verticalAlignment = when (p?.align) {
            SduiAlign.Center.raw -> Alignment.CenterVertically
            SduiAlign.End.raw -> Alignment.Bottom
            else -> Alignment.Top
        },
    ) {
        node.children.forEach { RenderChild(it, onAction, binding, this) }
    }
}

/**
 * Filho de Column: aplica flex/flexGrow -> weight. Uma List (LazyColumn) nao tem
 * altura intrinseca: num Column colapsaria/estouraria — trata como flexivel por
 * padrao pra preencher o espaco restante (igual ao host iOS).
 */
@Composable
private fun RenderChild(node: SduiNode, onAction: (SduiAction, SduiNode) -> Unit, binding: Map<String, String>?, scope: ColumnScope) {
    val isFillingList = node.type == SduiNodeType.List && node.list != null
    val grow = node.props?.flexGrow ?: node.props?.flex ?: (if (isFillingList) 1f else 0f)
    val childMod = if (grow > 0f) with(scope) { Modifier.weight(grow).fillMaxWidth() } else Modifier
    RenderNode(node, onAction, binding, childMod)
}

/** Filho de Row: aplica flex/flexGrow -> weight no escopo da linha. */
@Composable
private fun RenderChild(node: SduiNode, onAction: (SduiAction, SduiNode) -> Unit, binding: Map<String, String>?, scope: RowScope) {
    val grow = node.props?.flexGrow ?: node.props?.flex ?: 0f
    val childMod = if (grow > 0f) with(scope) { Modifier.weight(grow) } else Modifier
    RenderNode(node, onAction, binding, childMod)
}

// ── Resolucao de props / texto ────────────────────────────────────────────────

/** Responsivo: retorna a 1a variacao que casa (merge raso), senao props base. */
private fun resolveProps(node: SduiNode): SduiProps? {
    if (node.responsive.isEmpty()) return node.props
    // Headless/spike: sem TraitCollection — usa a base. (O merge esta testado em unit.)
    return node.props
}

/** Texto: se ha Bind["text"] e contexto de linha, usa o dado; senao props.text. */
private fun boundText(node: SduiNode, p: SduiProps?, binding: Map<String, String>?): String? {
    node.bind["text"]?.let { key -> binding?.get(key)?.let { return it } }
    return p?.text
}

// ── Aplicadores de props ────────────────────────────────────────────────────

private fun Modifier.applyBox(p: SduiProps?): Modifier {
    if (p == null) return this
    var m = this
    val shape = if (p.cornerRadius != null) RoundedCornerShape(p.cornerRadius.dp) else null
    if (shape != null) m = m.clip(shape)
    val bg = colorOf(p.background)
    if (bg != null) m = if (shape != null) m.background(bg, shape) else m.background(bg)
    val bc = colorOf(p.borderColor)
    if (bc != null && (p.borderWidth ?: 0f) > 0f) {
        m = if (shape != null) m.border(p.borderWidth!!.dp, bc, shape) else m.border(p.borderWidth!!.dp, bc)
    }
    return m
}

private fun Modifier.applyA11y(a: SduiA11y?): Modifier {
    if (a == null) return this
    return this.semantics {
        a.label?.let { contentDescription = it }
        if (a.role == 2) heading() // header
    }
}

private fun Modifier.paddingOf(p: SduiProps?): Modifier {
    val e = p?.padding ?: return this
    return this.padding(PaddingValues(start = e.left.dp, top = e.top.dp, end = e.right.dp, bottom = e.bottom.dp))
}

private fun spacing(p: SduiProps?): Arrangement.Vertical =
    (p?.spacing ?: 0f).let { if (it > 0f) Arrangement.spacedBy(it.dp) else Arrangement.Top }

private fun spacingH(p: SduiProps?): Arrangement.Horizontal =
    (p?.spacing ?: 0f).let { if (it > 0f) Arrangement.spacedBy(it.dp) else Arrangement.Start }

/** RGBA 0xRRGGBBAA -> Compose Color (ARGB). Mesmo formato do host iOS/Windows. */
private fun colorOf(rgba: Long?): Color? {
    if (rgba == null) return null
    val r = ((rgba shr 24) and 0xFF).toInt()
    val g = ((rgba shr 16) and 0xFF).toInt()
    val b = ((rgba shr 8) and 0xFF).toInt()
    val a = (rgba and 0xFF).toInt()
    return Color(red = r, green = g, blue = b, alpha = a)
}

private fun fontWeightOf(w: Int?): FontWeight = when (w) {
    SduiFontWeight.Medium.raw -> FontWeight.Medium
    SduiFontWeight.Semibold.raw -> FontWeight.SemiBold
    SduiFontWeight.Bold.raw -> FontWeight.Bold
    else -> FontWeight.Normal
}
