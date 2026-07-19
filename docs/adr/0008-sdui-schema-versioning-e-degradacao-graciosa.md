# ADR 0008 — SDUI: versionamento de schema + degradação graciosa (OTA-safe)

- **Status:** Aceito (implementado; Onda 1 fundacional)
- **Data:** 2026-07-19
- **Branch:** `feat/mabel-roadmap-schema` (base `feat/sdui-descriptor`)
- **Relacionado:** ADR 0001 (SDUI), ADR 0006 (OTA)

## Contexto

O descritor SDUI é entregue **Over-The-Air**: o guest evolui e passa a emitir um
schema mais novo que o **host instalado** entende. Sem contrato, um `type` de nó
novo — ou uma prop nova — quebraria o parse e a tela inteira sumiria. Isso não dá
pra corrigir "bolt-on" depois: a política precisa nascer no schema.

## Decisão

1. `SduiDocument.SchemaVersion` (já existia) formaliza a versão do wire.
   `SduiSchema.CurrentVersion = 2` marca esta Onda (v1 = Board; v2 = a11y,
   responsivo, List virtualizada, navegação, degradação).
2. **Contrato de degradação**, representável no nó:
   - **Props desconhecidas → ignoradas** (System.Text.Json e o decode Swift já
     tratam keys opcionais). Nunca quebram o parse.
   - **`type` de nó desconhecido** (ou `MinSchemaVersion` > versão do host) → o
     host aplica `SduiNode.Fallback` (`SduiUnknownFallback`): `RenderChildren`
     (default seguro — nós novos tendem a ser wrappers), `Placeholder` (dev), ou
     `Ignore`.
3. `SduiNode.MinSchemaVersion` declara o mínimo pra render fiel.
4. Utilitários verificáveis: `SduiNodeType.IsKnown()` e
   `SduiNode.ResolveFallback(hostSchemaVersion)`.
5. Opções JSON canônicas centralizadas em `SduiJson` (camelCase, enums como
   número, omite null) — compartilhadas por emissor e testes.

## Consequências

- OTA seguro: descritor "do futuro" parseia sem exceção; a subárvore conhecida
  sobrevive. Coberto por `SduiCompatibilityTests` (tipo 200, schema 99).
- Default `RenderChildren` favorece continuidade visual em vez de tela vazia.
- Enums permanecem numéricos (byte) — o host Swift decodifica `UInt8`; tolera
  valores fora da faixa nomeada sem quebrar.
- Trade-off: o host precisa implementar a resolução de fallback; o descritor só
  a **declara**.
