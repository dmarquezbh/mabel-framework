package com.mabel.board

import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.mabel.host.sdui.MabelSduiRoot
import com.mabel.host.sdui.SduiParser
import com.mabel.host.sdui.SDUI_TAG

// =============================================================================
// Mabel Board (Android) — app de prova do spike SDUI (schema v2).
// Carrega o MESMO board-sdui.json (Onda 2) usado pelo host iOS (assets/),
// decodifica pra arvore semantica e renderiza via MabelViewBuilder Compose
// (Host.Android): NavStack (home->detail), lista de 30 cards (itemTemplate +
// binding) e Fallback do no futuro type-200. Tap num card resolve o node.id
// semantico + dados de negocio, navega (push) e loga/Toast; Voltar faz pop.
// =============================================================================

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val json = assets.open("board-sdui.json").bufferedReader().use { it.readText() }
        val doc = SduiParser.parse(json)
        Log.i(SDUI_TAG, "descriptor loaded: schemaVersion=${doc.schemaVersion} rootId='${doc.root.id}' rootType=${doc.root.type}")

        setContent {
            MabelSduiRoot(doc) { action, node ->
                Log.i(SDUI_TAG, "APP received tap -> node.id='${node.id}' action='${action.name}' args=${action.args} credor='${node.props?.data?.get("credor")}'")
                Toast.makeText(this, "tap ${node.id} -> ${action.name}(${action.args})", Toast.LENGTH_SHORT).show()
            }
        }
    }
}
