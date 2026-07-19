# ADR 0011 — SDUI: listas virtualizadas / lazy

- **Status:** Aceito (implementado; Onda 1 fundacional)
- **Data:** 2026-07-19
- **Branch:** `feat/mabel-roadmap-schema`
- **Relacionado:** ADR 0001 (SDUI)

## Contexto

O tipo de nó `List` (0x05) existia como "coleção de filhos homogêneos", mas sem
semântica de reciclagem. Um host ingênuo materializaria N filhos — numa lista de
milhares de cards, isso estoura memória e latência. Faltava distinguir uma
lista **virtualizada** de um `VStack` estático.

## Decisão

`SduiNode.List` (`SduiListData`) dá semântica **lazy**:

- **`ItemTemplate`** — um único nó-template de linha (o único materializado no
  descritor, independentemente do total de linhas).
- **`Items`** — os DADOS das linhas (`SduiListItem`: `Id`, `Data`, `OnTap`),
  possivelmente uma **janela** (`Count` total, `WindowStart` offset).
- **`Virtualized`** (default `true`), `Axis`, `EstimatedItemExtent` pra
  reciclagem/scrollbar.
- **Binding** template→linha via `SduiNode.Bind` (prop-alvo → chave em
  `Data`); o host substitui por linha ao reciclar.

Distinção explícita: `VStack + Children[]` = N nós estáticos; `List + ListData`
= template + dados, host recicla (UICollectionView/LazyColumn).

## Consequências

- Descritor pequeno mesmo com listas enormes: 1 template + dados, não N nós.
- Paginação/streaming natural via janela (`Count`/`WindowStart`); o host pede
  mais ao chegar perto do fim.
- Round-trip coberto em `SduiRoundTripTests` (janela de 5000, template + 2 itens).
- Trade-off: introduz um mini-modelo de binding (`Bind`); mantido mínimo
  (chave→chave), execução fica no host.
