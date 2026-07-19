# ADR 0010 — SDUI: layout responsivo / adaptativo

- **Status:** Aceito (implementado; Onda 1 fundacional)
- **Data:** 2026-07-19
- **Branch:** `feat/mabel-roadmap-schema`
- **Relacionado:** ADR 0001 (SDUI), ADR 0004 (desktop)

## Contexto

Um único descritor precisa servir telas de tamanhos diferentes (iPhone compact,
iPad regular, split-view, landscape, e futuramente desktop). O schema v1 só tinha
`Width/Height/Flex/Spacing/Padding/Align` — insuficiente pra adaptar.

## Decisão

Duas alavancas, ambas no schema:

1. **Refinamentos diretos em `SduiProps`:**
   - `MinWidth/MaxWidth/MinHeight/MaxHeight`, `AspectRatio`.
   - Flexbox: `FlexGrow`, `FlexShrink`, `FlexBasis` (`Flex` v1 vira sinônimo de
     grow), `Wrap` (`SduiWrap`).
   - `SafeArea` (`SduiSafeArea`, flags top/right/bottom/left) pra notch/home bar.
2. **`SduiNode.Responsive`** — lista de `SduiResponsiveOverride`: variações de
   props condicionadas a `WidthClass`/`HeightClass` (`SduiSizeClass`
   compact/regular, espelha UIKit) e/ou breakpoint `MinContainerWidth`. O host
   escolhe a **primeira regra que casa** e faz **merge raso** sobre os props base.

## Consequências

- O mesmo descritor vira coluna única (compact) ou lado-a-lado (regular) sem o
  guest reemitir árvore.
- Merge raso é simples e previsível; o custo é que overrides parciais precisam
  repetir o campo alterado.
- Size classes semânticas mantêm o descritor platform-neutral; o host decide o
  que é compact/regular.
- Round-trip coberto em `SduiRoundTripTests`.
