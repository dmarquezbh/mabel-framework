package com.mabel.kanban

import androidx.test.core.app.ApplicationProvider
import com.mabel.host.sdui.SduiFallback
import com.mabel.host.sdui.SduiNavKind
import com.mabel.host.sdui.SduiNode
import com.mabel.host.sdui.SduiNodeType
import com.mabel.host.sdui.SduiParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

// =============================================================================
// Headless (JVM/Robolectric, SEM emulador): prova que o MESMO kanban-sdui.json
// (schema v2) que o host iOS consome decodifica na arvore semantica esperada no
// Android — NavStack (2 telas), lista de 30 cards com itemTemplate+binding, e o
// no futuro type-200 preservado (decode TOLERANTE) pra Fallback.
// Contrato compartilhado entre plataformas — o wire e identico.
// =============================================================================

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class SduiContractTest {

    private fun loadDoc() = SduiParser.parse(
        ApplicationProvider.getApplicationContext<android.content.Context>()
            .assets.open("kanban-sdui.json").bufferedReader().use { it.readText() }
    )

    private fun walk(n: SduiNode, f: (SduiNode) -> Unit) {
        f(n); n.children.forEach { walk(it, f) }
        n.list?.let { walk(it.itemTemplate, f) }
    }

    @Test
    fun `document is schema v2 with a NavStack of two routed screens`() {
        val doc = loadDoc()
        assertEquals(2, doc.schemaVersion)
        assertEquals("nav:root", doc.root.id)
        assertEquals(SduiNodeType.NavStack, doc.root.type)

        val screens = doc.root.children.filter { it.type == SduiNodeType.Screen }
        assertEquals("NavStack tem 2 telas", 2, screens.size)
        assertEquals(setOf("home", "detail"), screens.mapNotNull { it.nav?.route }.toSet())
    }

    @Test
    fun `home lists 30 cards via itemTemplate with stable ids and business data`() {
        val doc = loadDoc()
        var list: SduiNode? = null
        walk(doc.root) { if (it.type == SduiNodeType.List && it.list != null) list = it }
        assertNotNull("ha uma List com itemTemplate", list)

        val data = list!!.list!!
        // 30 operacoes (card:50000..card:50029) — declaradas no descritor.
        assertEquals(30, data.items.size)
        assertEquals(30, data.count)

        for (item in data.items) {
            assertTrue("id semantico estavel: ${item.id}", item.id.startsWith("card:"))
            assertNotNull("credor em ${item.id}", item.data["credor"])
            assertNotNull("valor em ${item.id}", item.data["valor"])
            assertNotNull("etapa em ${item.id}", item.data["etapa"])
        }

        // itemTemplate = Card acionavel que NAVEGA (push -> detail).
        val tpl = data.itemTemplate
        assertEquals(SduiNodeType.Card, tpl.type)
        assertEquals("open-card", tpl.onTap?.name)
        assertEquals(SduiNavKind.Push, tpl.onTap?.navigate?.kind)
        assertEquals("detail", tpl.onTap?.navigate?.route)

        // O template liga textos por Bind (credor/valor/etapa vem dos dados da linha).
        val binds = mutableListOf<String>()
        walk(tpl) { it.bind["text"]?.let { k -> binds.add(k) } }
        assertTrue("binds do template: $binds", binds.containsAll(listOf("credor", "valor", "etapa")))

        // spot-check do primeiro item
        val first = data.items.first { it.id == "card:50000" }
        assertEquals("Construtora ÁlAn S.", first.data["credor"])
        assertEquals("R$ 100k", first.data["valor"])
    }

    @Test
    fun `unknown type-200 node is preserved tolerantly for fallback`() {
        val doc = loadDoc()
        var future: SduiNode? = null
        walk(doc.root) { if (it.typeRaw == 200) future = it }
        assertNotNull("no futuro type-200 presente no descritor", future)
        assertNull("type-200 e desconhecido -> nao mapeia pra enum", future!!.type)
        assertEquals(SduiFallback.Placeholder, SduiFallback.from(future!!.fallback))
        // O no futuro traz um filho que a politica Placeholder NAO renderiza.
        assertTrue("no futuro tem filho oculto", future!!.children.isNotEmpty())
    }

    @Test
    fun `detail screen declares a back (pop) navigation`() {
        val doc = loadDoc()
        val detail = doc.root.children.first { it.nav?.route == "detail" }
        var back: SduiNode? = null
        walk(detail) { if (it.type == SduiNodeType.Button && it.onTap?.navigate != null) back = it }
        assertNotNull("tela de detalhe tem botao Voltar", back)
        assertEquals(SduiNavKind.Pop, back!!.onTap?.navigate?.kind)
    }
}
