# ADR 0009 — SDUI: acessibilidade no descritor

- **Status:** Aceito (implementado; Onda 1 fundacional)
- **Data:** 2026-07-19
- **Branch:** `feat/mabel-roadmap-schema`
- **Relacionado:** ADR 0001 (SDUI)

## Contexto

A tese do SDUI é "a11y de graça do nativo": VoiceOver, traits e Dynamic Type
vêm do SO porque o host instancia controles reais. Mas "de graça" só vale pro
que o controle já expõe (texto de um `UILabel`). Rótulos semânticos, papéis
(header/adjustable), hints de ação e ocultar decorativos **precisam vir no
descritor** — senão o leitor de tela lê texto cru ou nada.

## Decisão

Adicionar `SduiNode.A11y` (`SduiA11y`), platform-neutral:

- `Label`, `Hint`, `Value` (string) — anúncio, dica de ação, valor corrente.
- `Role` (`SduiA11yRole`: button, header, link, image, adjustable, search,
  summary, toggle, progress-indicator…) → o host mapeia pro trait nativo.
- `Traits` (`SduiA11yTraits`, flags: selected, disabled, updates-frequently…).
- `Hidden` (bool) → `accessibilityElementsHidden` pra decorativos.

Todos opcionais: ausência ⇒ o host usa o default do controle nativo.

## Consequências

- O guest pode enriquecer a semântica sem tocar em API de plataforma.
- Papéis/traits são **semânticos**, não `UIAccessibilityTraits`; cada host
  traduz (iOS hoje, Android/desktop depois).
- Round-trip coberto em `SduiRoundTripTests`.
- Trade-off: o guest carrega a responsabilidade de anotar; um lint futuro pode
  sinalizar nós interativos sem `Label`.
