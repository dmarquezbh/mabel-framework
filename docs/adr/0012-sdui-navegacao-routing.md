# ADR 0012 — SDUI: navegação / routing declarativo

- **Status:** Aceito (implementado; Onda 1 fundacional)
- **Data:** 2026-07-19
- **Branch:** `feat/mabel-roadmap-schema`
- **Relacionado:** ADR 0001 (SDUI)

## Contexto

Uma tela isolada não é um app. O descritor precisava expressar uma **pilha de
telas** e transições — sem o guest manipular imperativamente a
`UINavigationController` (o guest é platform-neutral e, no futuro, WASM).

## Decisão

- **Estrutura:** novo tipo de nó `NavStack` (0x0E) → `UINavigationController`.
  Hospeda `Screen`s; cada `Screen` carrega `SduiNode.Nav` (`SduiNav`: `Route`
  nomeada, `Title`, `Modal`, `HidesNavBar`).
- **Ações:** `SduiAction.Navigate` (`SduiNavigate`) declara a transição —
  `SduiNavKind` push/pop/replace/root/pop-to — com `Route` alvo + `Params`
  (deep-link args). O host aplica à pilha nativa.
- **Deep-linking:** uma URL externa vira `{Route, Params}`; o host reconstrói a
  pilha via rotas nomeadas.

## Consequências

- Fluxo multi-tela expressável 100% no descritor; o app recebe eventos de
  navegação de forma declarativa.
- `NavStack` é um tipo novo → protegido pela degradação graciosa do ADR 0008
  (host antigo cai em `RenderChildren` e ainda mostra a tela raiz).
- Round-trip coberto em `SduiRoundTripTests` (stack com 2 Screens + push com
  params).
- Trade-off: a semântica de apresentação (modal vs push, animações) fica a cargo
  do host; o descritor só declara intenção.
