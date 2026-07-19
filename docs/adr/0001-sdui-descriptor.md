# ADR 0001 — Mabel vira SDUI (descritor semântico → controles nativos)

- **Status:** Proposto (design; aguardando OK pra implementar o host iOS)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/sdui-descriptor`

## Contexto

O Mabel nasceu como um **display-list de pixels**: o guest emitia `RenderCommand`
(`RenderOp.Rect/Text/RoundRect/...`, ver `Protocol.cs`) e cada host pintava via
Core Graphics / SkiaSharp. O spike do Kanban iOS provou o render — mas o app abria
**estático**: um canvas de pixels não tem scroll, hit-testing, acessibilidade,
seleção de texto nem IME. Tudo isso teria que ser reimplementado à mão (caminho
"Flutter-like"), o que só se paga pra visual bespoke.

Três rotas avaliadas:
1. **Canvas-tudo** — reimplementa texto/scroll/a11y do zero. Descartada.
2. **WebView nativo (Tauri-like)** — reusa web, mas não é "nativo". Descartada.
3. **SDUI: descritor semântico → controles nativos** — **escolhida.**

Insight: canvas + overlay semântico (o "hit-region sobre canvas", N2 anterior)
é o pior dos dois mundos — mantém renderer de pixels E descritor. Escolher UM
eixo; o semântico venceu.

Constraint inegociável: **build iOS sem Mac, via xtool.** Isso descarta MAUI e
Flutter (ambos exigem Mac/Xcode pra iOS). Resta **Swift UIKit/SwiftUI
hand-rolled**, que o xtool já builda (o IPA hello-world provou).

## Decisão

O Mabel passa a emitir uma **árvore semântica de UI** (SDUI). O guest descreve
**controles**, não desenhos. O host mapeia a árvore pra controles nativos reais.

### Schema (v1)

`Mabel.Wasi.Protocol/Sdui/Descriptor.cs`. Envelope:

```
SduiDocument { SchemaVersion:int, Root:SduiNode }
SduiNode     { Id:string, Type:SduiNodeType, Props?, Children?, OnTap? }
SduiProps    { layout (Spacing/Padding/Align/Width/Height/Flex/Axis),
               box (Background/CornerRadius/BorderColor/BorderWidth),
               text (Text/FontSize/Color/Weight), Src, Value, Data }
SduiAction   { Name:string, Args? }
```

Tipos de nó v1 (suficientes pro Kanban Kanban):
`Screen, VStack, HStack, ScrollView, List, Card, Text, Button, Image, Badge,
ProgressBar, Divider, Spacer`.

Princípios:
- **`Id` semântico e estável** (`card:50231`, `column:ORIGINACAO/Cadastro`).
  No tap, o host devolve `{action, args, id, data}` — **zero coordenadas de pixel**.
- **Cores** = RGBA `0xRRGGBBAA` (mesmo formato do `RenderCommand`, continuidade).
- **Layout flexbox-like** platform-agnostic (stack + spacing + align + flex);
  o host traduz pra Auto Layout / SwiftUI.
- **v1 é "árvore expandida"**: sem motor de template/data-binding. O guest emite
  os cards já expandidos (o `board_gen` já tem os dados). Templates de item p/
  `List` reciclável ficam pra v2.

### Modelo de eventos

Substitui o `InputEvent` (coords de toque) por **ações semânticas**. Um nó com
`OnTap` vira clicável; o controle nativo (`.touchUpInside`, `didSelectItemAt`)
dispara `OnTap.Name` + `node.Id` + `Props.Data` de volta pro app. Scroll, foco,
VoiceOver e Dynamic Type não passam pelo protocolo — são do controle nativo.

### Mapeamento iOS (`Mabel.Host.Ios`)

Um **`MabelViewBuilder`** (Swift) percorre o descritor e instancia UIKit:

| Nó          | Controle nativo                              |
|-------------|----------------------------------------------|
| Screen      | `UIView` raiz (background)                    |
| ScrollView  | `UIScrollView` (eixo por `Props.Axis`)        |
| VStack/HStack | `UIStackView` (axis/spacing/alignment)      |
| List        | `UICollectionView` (compositional layout)     |
| Card        | `UIControl` subclass → tap → `OnTap`+`Id`      |
| Text        | `UILabel` (fonte/peso/cor)                     |
| Button      | `UIButton`                                     |
| Image       | `UIImageView` (asset ou SF Symbol via `Src`)   |
| Badge       | `UILabel` com fundo/corner pill                |
| ProgressBar | `UIProgressView` (`Props.Value`)               |
| Divider     | `UIView` 1px                                   |

- `MabelView` (SwiftUI `UIViewRepresentable`) passa a retornar a raiz construída
  (dentro de scroll), não mais o `MabelCanvasView`. O `.draw()` de pixels sai do
  caminho do app (arquivo preservado pra referência do display-list, não deletado).
- Tap nativo → callback `onNodeAction(action, id, data)` → app.

### board_gen (.NET)

Passa a construir um `SduiDocument` (árvore de `SduiNode`) em vez do display-list
de `RenderCommand`. O modelo de dados (grupos/etapas/cards) e os metadados já
existem — reaproveitados. Emite `kanban-sdui.json`.

## Prova end-to-end mínima

Kanban Kanban em **controles nativos de verdade**:
- `ScrollView` horizontal de colunas;
- cada coluna = `VStack`/`List` de `Card`s;
- **scroll real** (nativo) + **tap num card logando o `cardId`** (nativo).

Isso valida descritor→nativo + eventos + scroll, sem um pixel de canvas.

## Escopo / não-metas (v1)

- **É:** árvore estática expandida, ~13 tipos de nó, tap→ação, scroll nativo, iOS.
- **Não é (ainda):** templates de item recicláveis, animações, data-binding
  reativo, diffing incremental de árvore, host Android (estrutura existe, fica
  p/ depois), transporte binário WASI (v1 = JSON).

## Consequências

- (+) Feel nativo + a11y/scroll/IME grátis; host burro e fino.
- (+) `Id` semântico casa com analytics/testes (tap em `card:50231`).
- (−) Poder expressivo limitado ao conjunto de nós (visual bespoke exigiria
  estender o schema ou um nó `Canvas` de escape — fora do escopo v1).
- (−) `board_gen` e `Mabel.Host.Ios` precisam de reescrita (display-list → árvore).
