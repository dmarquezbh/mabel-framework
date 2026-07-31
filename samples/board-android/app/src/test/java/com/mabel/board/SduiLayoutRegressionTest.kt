package com.mabel.board

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.getUnclippedBoundsInRoot
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.unit.height
import androidx.compose.ui.unit.width
import com.mabel.host.sdui.MabelSduiRoot
import com.mabel.host.sdui.SduiParser
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

// =============================================================================
// Regressao de layout do host Android.
//
// 1) Align=Stretch (SduiAlign.Stretch, raw 3) esticava? NAO: o `when` do
//    RenderColumn/RenderRow mapeava Center/End e caia no else -> Alignment.Start,
//    entao Stretch era um no-op SILENCIOSO. O host Windows honra Stretch e ate o
//    adota como default (MabelWindowsBuilder: `?? SduiAlign.Stretch`), logo os dois
//    hosts divergiam no mesmo descritor.
//
// 2) Text tinha maxLines=1 fixo, truncando qualquer rotulo mais largo que o
//    container — e o schema v1 nao tem prop pro guest pedir quebra de linha.
// =============================================================================

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SduiLayoutRegressionTest {

    @get:Rule
    val compose = createComposeRule()

    /** Screen > VStack(align) > Card > Text(label). */
    private fun docWith(align: Int, label: String) = SduiParser.parse(
        """
        {"schemaVersion":1,"root":{"id":"screen:t","type":1,"children":[
          {"id":"stack:t","type":2,"props":{"align":$align},"children":[
            {"id":"card:t","type":6,"onTap":{"name":"tap"},"children":[
              {"id":"text:t","type":7,"props":{"text":"$label","fontSize":14}}
            ]}
          ]}
        ]}}
        """.trimIndent()
    )

    @Test
    fun `align Stretch faz o filho do Column ocupar a largura toda`() {
        compose.setContent { MabelSduiRoot(docWith(align = 3, label = "alvo")) { _, _ -> } }
        compose.waitForIdle()

        val rootWidth = compose.onRoot().getUnclippedBoundsInRoot().width
        val cardWidth = compose.onNode(hasClickAction()).getUnclippedBoundsInRoot().width

        // Com Stretch o Card preenche a largura. Antes da correcao ficava do
        // tamanho do conteudo ("alvo" ~ poucos dp).
        assertTrue(
            "Stretch deveria esticar: card=$cardWidth root=$rootWidth",
            cardWidth.value >= rootWidth.value * 0.98f,
        )
    }

    @Test
    fun `sem Stretch o filho continua do tamanho do conteudo`() {
        compose.setContent { MabelSduiRoot(docWith(align = 0, label = "alvo")) { _, _ -> } }
        compose.waitForIdle()

        val rootWidth = compose.onRoot().getUnclippedBoundsInRoot().width
        val cardWidth = compose.onNode(hasClickAction()).getUnclippedBoundsInRoot().width

        // Align=Start nao deve virar fillMaxWidth — a correcao e especifica do Stretch.
        assertTrue(
            "Start nao deveria esticar: card=$cardWidth root=$rootWidth",
            cardWidth.value < rootWidth.value * 0.9f,
        )
    }

    @Test
    fun `Text quebra em varias linhas em vez de truncar`() {
        val longo = "Controles nativos via descriptor SDUI, renderizados pelo host " +
            "com os proprios controles do sistema operacional, sem WebView nenhum."

        // Os dois textos no MESMO descritor: o harness do Compose so permite um
        // setContent por teste, e comparar no mesmo render evita variacao de largura.
        val doc = SduiParser.parse(
            """
            {"schemaVersion":1,"root":{"id":"screen:t","type":1,"children":[
              {"id":"stack:t","type":2,"props":{"align":3},"children":[
                {"id":"text:curto","type":7,"props":{"text":"curto","fontSize":14}},
                {"id":"text:longo","type":7,"props":{"text":"$longo","fontSize":14}}
              ]}
            ]}}
            """.trimIndent()
        )

        compose.setContent { MabelSduiRoot(doc) { _, _ -> } }
        compose.waitForIdle()

        compose.onNodeWithText(longo).assertIsDisplayed()
        val alturaCurta = compose.onNodeWithText("curto").getUnclippedBoundsInRoot().height
        val alturaLonga = compose.onNodeWithText(longo).getUnclippedBoundsInRoot().height

        // Com maxLines=1 as duas alturas eram identicas (1 linha) e o texto era
        // cortado. Quebrando, o texto longo ocupa varias linhas.
        assertTrue(
            "texto longo deveria ocupar mais de uma linha: longo=$alturaLonga curto=$alturaCurta",
            alturaLonga.value > alturaCurta.value * 1.5f,
        )
    }
}
