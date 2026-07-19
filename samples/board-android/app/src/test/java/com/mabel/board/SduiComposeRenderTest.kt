package com.mabel.board

import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasScrollToNodeAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.test.core.app.ApplicationProvider
import com.mabel.host.sdui.SduiAction
import com.mabel.host.sdui.SduiNode
import com.mabel.host.sdui.MabelSduiRoot
import com.mabel.host.sdui.SduiNodeType
import com.mabel.host.sdui.SduiParser
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

// =============================================================================
// Headless (Robolectric + Compose, SEM emulador/tela): prova que o
// MabelViewBuilder Compose INSTANCIA CONTROLES NATIVOS reais a partir do
// descritor schema v2 — NavStack, lista de 30 cards (itemTemplate+binding) e o
// no futuro type-200 (Fallback). Tap num Card NATIVO resolve node.id semantico
// E executa navegacao declarativa (push/pop) — sem coordenadas de pixel.
// =============================================================================

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SduiComposeRenderTest {

    @get:Rule
    val compose = createComposeRule()

    private fun doc() = SduiParser.parse(
        ApplicationProvider.getApplicationContext<android.content.Context>()
            .assets.open("board-sdui.json").bufferedReader().use { it.readText() }
    )

    @Test
    fun `home renders native header and applies type-200 fallback hiding its child`() {
        compose.setContent { MabelSduiRoot(doc()) { _, _ -> } }
        compose.waitForIdle()

        // Header nativo da home (NavStack mostra a 1a tela).
        compose.onNodeWithText("Operações").assertExists()

        // Fallback do no futuro type-200: placeholder visivel...
        compose.onAllNodesWithText("não suportado", substring = true).onFirst().assertExists()
        // ...e o filho do no futuro NAO e renderizado (politica Placeholder).
        compose.onNodeWithText("(oculto — filho do nó futuro)").assertDoesNotExist()

        // Primeiro card ja e um controle nativo com texto de negocio real.
        compose.onNodeWithText("Construtora ÁlAn S.").assertExists()
    }

    @Test
    fun `list holds 30 cards and tapping one pushes detail then back pops home`() {
        val d = doc()

        // Autoridade do "30 cards": 30 itens declarados mapeiam pro template.
        var listCount = 0
        fun walk(n: SduiNode, f: (SduiNode) -> Unit) { f(n); n.children.forEach { walk(it, f) }; n.list?.let { walk(it.itemTemplate, f) } }
        walk(d.root) { if (it.type == SduiNodeType.List && it.list != null) listCount = it.list!!.items.size }
        assertEquals(30, listCount)

        val taps = mutableListOf<Pair<SduiAction, SduiNode>>()
        compose.setContent { MabelSduiRoot(d) { a, n -> taps.add(a to n) } }
        compose.waitForIdle()

        // Tap no primeiro card (valor unico R$ 100k identifica card:50000). O Card
        // e um no de semantica MERGED: o texto do filho sobe pro no clicavel.
        compose.onAllNodes(hasClickAction() and hasText("R$ 100k", substring = true)).onFirst().performClick()
        compose.waitForIdle()

        // O app recebe o node.id SEMANTICO + a navegacao declarativa (push detail).
        assertEquals(1, taps.size)
        val (action, node) = taps.first()
        assertEquals("card:50000", node.id)
        assertEquals("open-card", action.name)
        assertEquals("detail", action.navigate?.route)
        assertEquals("Construtora ÁlAn S.", node.props?.data?.get("credor"))

        // Push efetivou: tela de detalhe nativa aparece, home saiu.
        compose.onNodeWithText("Detalhe da operação").assertExists()
        compose.onNodeWithText("Operações").assertDoesNotExist()

        // Pop: botao Voltar volta pra home.
        compose.onNodeWithText("← Voltar").performClick()
        compose.waitForIdle()
        compose.onNodeWithText("Operações").assertExists()
    }

    @Test
    fun `virtualized list renders the 30th card after scrolling`() {
        compose.setContent { MabelSduiRoot(doc()) { _, _ -> } }
        compose.waitForIdle()

        // O 30o card (card:50029, valor unico R$ 473k) so compoe apos rolar —
        // prova a virtualizacao NATIVA (LazyColumn) da lista inteira.
        compose.onAllNodes(hasScrollToNodeAction()).onFirst().performScrollToNode(hasText("R$ 473k"))
        compose.waitForIdle()
        compose.onNodeWithText("R$ 473k").assertExists()
    }
}
