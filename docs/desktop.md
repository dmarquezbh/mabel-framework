# Mabel — Alvo desktop (Windows / Linux)

> **Fase 3.** Contrato/design. Irmão do ADR 0001 (SDUI), 0002 (capabilities) e
> 0003 (HMR). Decisão resumida em `docs/adr/0004-desktop.md`.

## 1. Tese: Mabel é mobile **E** desktop

O mesmo insight que sustenta o Mabel no celular vale no PC: **um módulo WASM
poliglota** (o app) + **um host fino por plataforma** que traduz um **descritor SDUI**
em **controles nativos**. Trocar iOS/Android por Windows/Linux muda só o host — o
guest, o descritor SDUI e o contrato WIT de capabilities são **os mesmos**. Desktop
não é um port; é mais um host da mesma arquitetura.

Alvos desktop: **Windows e Linux**. **macOS-desktop fica "A CONFIRMAR"** (não
"bloqueado"): o xtool já prova build/assinatura iOS sem Mac, e macOS-desktop sem Mac é
**plausível** pela mesma família de técnicas — cross-compile Swift/AppKit + assinatura via
`apple-codesign`/`rcodesign` no Linux + notarização via `notarytool`/API. Mas **não é um
caminho pavimentado do xtool hoje** → precisa de um spike próprio antes de afirmar. Não
tratar como resolvido. Windows/Linux vêm primeiro; macOS entra depois como mais um host,
sem mudar o modelo.

## 2. Por que desktop importa (três motivos)

1. **É o loop primário de HMR.** No desktop o runtime WASM tem **JIT** (wasmtime /
   Cranelift) — sem a proibição de JIT do iOS. Instanciar/trocar módulo é barato:
   **edita → vê na hora, sem device, sem deploy**. O desktop é onde o dev passa o dia;
   iOS/Android entram pra validar no aparelho. Ver `docs/hmr-e-estado.md`.
2. **É um alvo real.** Apps Mabel rodam no desktop de verdade (ex.: o Kanban como app
   de PC), não só como preview. Mesmo binário conceitual, controles nativos de desktop.
3. **Valida a tese "1 wasm → N hosts nativos".** Uma terceira família de host
   (depois de iOS UIKit e Android) prova que o descritor SDUI é genuinamente
   platform-agnostic, não um proxy disfarçado de UIKit.

## 3. Arquitetura do host desktop

```
  guest mabel.wasm  (C#/Blazor, Go, Rust — o MESMO do mobile)
        │  descritor SDUI (árvore semântica)  +  chamadas de capability (WIT)
        ▼
  Host desktop .NET
    ├─ runtime WASM: wasmtime (JIT) via Wasmtime.NET      ← hot-swap trivial
    ├─ MabelViewBuilder(desktop): descritor → controles nativos
    └─ implementações de capability desktop (arquivo, clipboard, notificação…)
        │
        ▼
  Toolkit nativo do SO
    ├─ Windows: WinUI 3 (WinApp SDK)  [ou Win32/WPF]
    └─ Linux:   GTK4                    [ou Qt]
```

- **Host em .NET** é o encaixe natural: o Mabel já é .NET (CLI, renderer, protocolo),
  e um host .NET embute um runtime WASM (`wasmtime` via **Wasmtime.NET**) e dirige o
  toolkit de UI. O host continua **fino**: só instancia o módulo, entrega o descritor
  ao view-builder e liga eventos/capabilities.
- **Runtime com JIT (wasmtime):** desktop **não** tem o ban de JIT do iOS. Full speed,
  hot-swap barato — habilita o loop de HMR primário (ADR 0003).

## 4. SDUI → controles nativos de desktop

O mesmo descritor (`Mabel.Wasi.Protocol/Sdui/Descriptor.cs`, 13 tipos de nó do ADR
0001) mapeia pra controles nativos de desktop — **não** canvas, **não** webview:

| Nó SDUI | Windows (WinUI 3) | Linux (GTK4) |
|---|---|---|
| Screen | `Window` / root `Grid` | `GtkWindow` / root `GtkBox` |
| VStack / HStack | `StackPanel` (Orientation) | `GtkBox` (orientation) |
| ScrollView | `ScrollViewer` | `GtkScrolledWindow` |
| List | `ItemsRepeater` / `ListView` | `GtkListView` (factory) |
| Card | `Button`/`Border` clicável | `GtkButton` / `GtkFlowBoxChild` |
| Text | `TextBlock` | `GtkLabel` |
| Button | `Button` | `GtkButton` |
| Image | `Image` (asset id / glyph) | `GtkPicture` / `GtkImage` |
| Badge | `Border` + `TextBlock` (pill) | `GtkLabel` estilizado (CSS) |
| ProgressBar | `ProgressBar` (Value) | `GtkProgressBar` |
| Divider | `Border`/`Rectangle` 1px | `GtkSeparator` |
| Spacer | `Grid`/`StackPanel` spacer | espaço flexível no `GtkBox` |

Layout flexbox-like (spacing/padding/align/flex/axis) traduz pra o layout nativo do
toolkit. Eventos seguem o modelo do ADR 0001: nó com `OnTap` vira clicável; o clique
nativo devolve `{action, id, data}` — zero coordenada de pixel. Scroll, foco, seleção
de texto, teclado e acessibilidade vêm **de graça** do controle nativo.

## 5. Escolha do toolkit — tradeoff explícito

Duas filosofias, e a decisão precisa ser consciente porque toca a **tese** do projeto
("controles **nativos do SO**"):

- **Toolkit nativo do SO (WinUI 3 / GTK4):** controles de verdade do sistema — o feel,
  a11y e temas do SO vêm prontos. **Fiel à tese.** Custo: dois view-builders de host
  (um por SO) e dependências específicas de plataforma.
- **Toolkit cross-platform de render próprio (Avalonia):** um host único Win+Linux(+mac)
  em .NET, ergonômico pra bring-up. Custo: Avalonia **desenha os próprios controles**
  (via Skia) — ou seja, é um caminho **canvas-like**, que é justamente o que o ADR 0001
  rejeitou no mobile pelo *feel* e pela a11y. No desktop a barra é mais baixa (usuários
  toleram temas próprios), mas não é "controle nativo do SO" no sentido estrito.

**Recomendação:** mirar **toolkit nativo do SO (WinUI 3 no Windows, GTK4 no Linux)**
como alvo de fidelidade, honrando a tese. **Permitir Avalonia como host único
pragmático** pra bring-up inicial e pra um modo **preview** (o dev vê a árvore rápido
enquanto os view-builders nativos amadurecem). Ou seja: Avalonia como andaime/preview;
WinUI/GTK como destino. Decisão final fica pro Daniel (ver §8).

## 6. Capabilities no desktop

O **mesmo contrato WIT** do ADR 0002 (`package mabel:capabilities`) vale no desktop;
muda a implementação do host e a tabela de disponibilidade:

| Capability | Desktop (Windows / Linux) | Observação |
|---|---|---|
| clipboard | ✅ nativo (WinUI Clipboard / GTK) | direto |
| share | ✅ share sheet do SO (onde houver) / fallback "salvar" | varia por SO |
| notifications (local) | ✅ toast (Windows) / libnotify (Linux) | sem push, igual ao free mobile |
| secure-storage | ✅ DPAPI (Windows) / libsecret-keyring (Linux) | por-app |
| camera / photo | ✅ webcam / file picker | file picker é o caminho comum no PC |
| location | ⚠️ limitado (Wi-Fi/IP) | menos preciso que mobile; muitas vezes não usado |
| biometrics | ⚠️ Windows Hello; Linux varia | opcional |
| haptics | ❌ no-op | desktop não tem; retorna `Ok`/no-op |

As capabilities mobile-only (haptics) viram **no-op** que respondem `Ok` sem efeito —
o guest não precisa de código condicional por plataforma além do que já faz por
manifesto. O manifesto (ADR 0002 §4) continua sendo a fonte de autoridade e das
usage-strings (no desktop, os prompts de consentimento são os do próprio SO).

## 7. Superfície específica de desktop (fora do escopo v1)

Desktop tem conceitos que o mobile não tem e que o SDUI v1 ainda não modela. Ficam
registrados como **futuro**, não v1:

- **Janelas:** redimensionamento, múltiplas janelas, min/max, tamanho mínimo.
- **Menus e atalhos de teclado:** menu bar, context menu, acceleradores.
- **Ponteiro:** hover, cursores, right-click, drag-and-drop.
- **Densidade/responsividade:** telas grandes exigem layouts que reflowam (o Kanban em
  PC não é só a coluna do celular esticada).

v1 do desktop mira **paridade com o mobile** (renderiza a mesma árvore SDUI com scroll
e tap nativos); a extensão do schema pra janelas/menus/teclado é fase posterior.

## 7b. Distribuição + auto-update (diferencial)

Duas camadas (ver também `docs/ota.md`):

- **Conteúdo (WASM + descritores) = OTA do servidor → shell recarrega:** instantâneo,
  minúsculo, sem reinstalar, sem restart (hot-swap). **No desktop é 100% LIVRE** — não há
  loja obrigatória (Win/Linux/macOS-direto), então o cinza 2.5.2 do iOS **não existe**: OTA
  de descritor **e** de lógica-wasm liberado.
- **Shell nativo (raro) = updater padrão por-OS:** Windows: MSIX / Squirrel.Windows; Linux:
  **AppImage + AppImageUpdate (delta/zsync)** / Flatpak / Snap / apt; macOS: **Sparkle**
  (⚠️ precisa notarização — amarra no spike macOS via API `notarytool`).

**Diferencial:** Electron/Tauri **re-baixam o binário inteiro** a cada update; o Mabel
**re-baixa só o conteúdo** (KB, instantâneo, sem fechar). **Robustez:** canais
stable/beta/canary + rollout gradual + rollback + **updates assinados** (verifica assinatura
antes de aplicar; gerência de chave).

## 8. Escopo / não-metas e decisões pendentes

**É (design):** host desktop .NET embutindo wasmtime (JIT); mapa SDUI → WinUI/GTK;
desktop como loop primário de HMR; capabilities desktop reusando o WIT; macOS adiado.

**Não é (ainda):** implementação dos view-builders; escolha final WinUI+GTK vs
Avalonia; schema SDUI pra janelas/menus/teclado/hover/DnD; empacotamento/instalador
(MSIX, AppImage/Flatpak); macOS-desktop.

**Decisões pendentes (Daniel):**
1. **Toolkit:** WinUI 3 + GTK4 (fiel à tese, dois view-builders) vs Avalonia (host
   único, render próprio) vs Avalonia-como-preview + nativo-como-destino (recomendado).
2. **Ordem de bring-up:** Windows primeiro (mais provável no dia a dia) ou Linux
   primeiro (onde já roda o WSL/dev)?
3. **wasmtime via Wasmtime.NET** é o runtime desktop — confirmar vs alternativas
   (Wasmer.NET). Recomendo wasmtime pela maturidade do Cranelift e do suporte WASI.
