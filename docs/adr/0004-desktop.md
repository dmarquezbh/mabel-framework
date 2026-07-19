# ADR 0004 — Desktop como alvo de primeira classe (Windows / Linux)

- **Status:** Proposto (design; sem macOS por ora)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-arch-consolidation`
- **Irmão de:** ADR 0001 (SDUI), 0002 (capabilities), 0003 (HMR). Design em `docs/desktop.md`.

## Contexto

A arquitetura do Mabel — **1 wasm poliglota + host fino por plataforma + descritor
SDUI → controles nativos** — não é intrinsecamente mobile. O mesmo modelo serve
desktop trocando só o host. Além de ser um alvo real (apps Mabel de PC), o desktop
resolve um problema de DX: no desktop o runtime WASM tem **JIT** (sem o ban do iOS),
então o inner loop de HMR é imediato — edita, vê, sem device nem deploy.

Restrições: **sem Mac** (mesma trave do ADR 0001). Windows e Linux primeiro.
**macOS-desktop = "A CONFIRMAR"** (não bloqueado): plausível sem Mac via cross-compile
Swift/AppKit + `apple-codesign`/`rcodesign` + notarização por API, mas não é caminho
pavimentado do xtool hoje → precisa de spike. Host em .NET (encaixa no stack existente).
Deve reusar o descritor SDUI (ADR 0001) e o WIT de capabilities (ADR 0002) sem alterações.

## Decisão

1. **Desktop é alvo de primeira classe**, ao lado de iOS e Android. Windows e Linux
   primeiro; **macOS-desktop "a confirmar"** (spike de build sem-Mac antes de prometer).
2. **Host desktop em .NET** embutindo **wasmtime (JIT)** via Wasmtime.NET. Host fino:
   instancia o módulo, roda o `MabelViewBuilder` desktop (descritor → controles) e liga
   eventos/capabilities. Hot-swap barato → **desktop é o loop primário de HMR** (ADR 0003).
3. **Controles nativos do SO** como alvo de fidelidade: **WinUI 3** (Windows) e **GTK4**
   (Linux). **Avalonia** permitido como **host único pragmático / modo preview** durante
   o bring-up (custo: render próprio, não controle do SO — andaime, não destino).
4. **Mesmo SDUI e mesmo WIT** dos móveis. Capabilities mobile-only (haptics) viram
   no-op no desktop; o manifesto (ADR 0002) segue como fonte de autoridade.
5. Conceitos só-de-desktop (janelas, menus, teclado, hover, drag-and-drop, reflow
   responsivo) ficam **fora do v1**; v1 mira paridade com o mobile (mesma árvore,
   scroll+tap nativos).

## Alternativas consideradas

- **Só mobile (desktop = fora):** perderia o melhor loop de HMR e um alvo real, e
  deixaria a tese "1 wasm → N hosts" sem uma terceira prova. Rejeitada.
- **Avalonia como destino único (Win+Linux+mac num host só):** ergonômico, mas
  Avalonia desenha os próprios controles (Skia) — caminho canvas-like que o ADR 0001
  rejeitou no mobile pelo feel/a11y. Mantido só como preview/andaime, não destino.
- **WebView desktop (Tauri-like):** contradiz a tese "sem webview". Rejeitada (igual
  ADR 0001).
- **Host nativo não-.NET (C++/Rust dirigindo Win32/GTK):** possível, mas joga fora o
  encaixe .NET do stack e duplicaria infra. Rejeitada por ora.

## Consequências

- (+) Inner loop de dev imediato (JIT, sem device) — o desktop vira a bancada do dev.
- (+) Terceira família de host valida o SDUI como genuinamente platform-agnostic.
- (+) Reuso total do descritor e do WIT; guest idêntico ao mobile.
- (−) Dois view-builders de host (WinUI + GTK) pra fidelidade nativa — mais superfície.
- (−) SDUI v1 não cobre janelas/menus/teclado; apps desktop ricos esperam extensão de
  schema (fase posterior).
- (−) macOS-desktop fica "a confirmar" (depende de um spike de build sem-Mac).

## A validar / decidir (Daniel)

1. **Toolkit:** WinUI 3 + GTK4 (fiel, dois builders) vs Avalonia (único, render
   próprio) vs Avalonia-preview + nativo-destino (recomendado).
2. **Ordem de bring-up:** Windows ou Linux primeiro.
3. **Runtime:** wasmtime via Wasmtime.NET (recomendado) vs Wasmer.NET.
