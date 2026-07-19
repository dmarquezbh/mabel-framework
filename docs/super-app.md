# Mabel — Plataforma de super-app (host + mini-apps WASM)

> **Pilar de plataforma.** Este é o enquadramento de topo do Mabel: não é só um
> framework de UI cross-platform, é a **plataforma de super-app poliglota da PJUS**.
> Irmão dos ADRs 0001–0004; decisão em `docs/adr/0005-super-app.md`.

## 1. A tese organizacional

> **Cada projeto/time da PJUS entrega o seu mini-app; o super-app PJUS renderiza todos.**

Um único app **PJUS** na loja/no device. As funcionalidades **não** são telas
hard-coded desse app — são **mini-apps WASM** independentes, cada um dono de um time,
que o shell nativo carrega e renderiza. Adicionar ou atualizar uma feature = publicar
um novo `.wasm` + descritor, **sem reinstalar** o app.

Isto reusa a arquitetura já desenhada (SDUI + WASM + capabilities WIT) e a eleva de
"como fazer um app cross-platform" para "**como a PJUS entrega software no device**".

### Pilares

- **Um app, muitas features.** PJUS é o app; Board, Aria, e o que cada time construir
  são mini-apps dentro dele. Crescem sem passar de novo pela loja (ver `docs/ota.md`).
- **Poliglota por time.** Cada time no seu stack — Opera em .NET/Blazor, outro em
  Go/Rust — e **todos emitem o mesmo descritor SDUI**. A plataforma democratiza: não
  obriga ninguém a adotar .NET (ver `docs/autoria-poliglota.md`).
- **Compartilhado pelo super-app.** Identidade/auth (device-code/OBO — login **uma
  vez**, todos os mini-apps herdam), capabilities (câmera/GPS/notif via WIT), storage,
  e a navegação/launcher entre mini-apps.
- **Sandbox por mini-app.** Cada mini-app roda isolado no seu próprio sandbox WASM —
  um projeto não lê a memória nem quebra o outro; autoridade só pelo que o manifesto
  concede (ADR 0002). É o modelo de segurança certo pra código de muitos times num app.
- **Registry de mini-apps.** O super-app lista e carrega os mini-apps publicados
  (baked no build via AOT, ou dinâmico interno — ver §4 e `docs/ota.md`).

## 2. Por que WASM é o modelo certo pra super-app

O padrão "super-app + mini-programs" é conhecido (WeChat, Alipay). Eles rodam
mini-programs em **JavaScript**. O Mabel faz o mesmo padrão com **WASM**, o que é
estritamente melhor pro caso PJUS:

- **Isolamento mais forte:** cada mini-app é um módulo WASM com memória linear própria;
  o sandbox é a fronteira do módulo, não uma convenção de linguagem.
- **Poliglota:** mini-program WeChat = JS obrigatório. Mini-app Mabel = qualquer
  linguagem que compile pra WASM (por time).
- **Capability-gated por construção:** o mini-app só toca o SO pelo que o manifesto
  declara e o host injeta (ADR 0002) — least authority auditável.
- **Controles nativos, não webview:** o mini-program WeChat renderiza numa webview; o
  mini-app Mabel emite descritor SDUI → **controles nativos** (feel/a11y do SO).

## 3. Anatomia do super-app

```
  ┌───────────────────────────────────────────────────────────────┐
  │  App PJUS = SHELL NATIVO (o host)                              │
  │                                                               │
  │  launcher / navegação entre mini-apps                         │
  │  identidade & auth compartilhada  (device-code / OBO)          │
  │  capabilities compartilhadas      (câmera/GPS/notif via WIT)   │
  │  storage compartilhado + storage por-mini-app (sandbox)        │
  │  mensageria entre mini-apps       (mediada pelo shell)         │
  │  registry + gerência de runtime WASM (load/unload/hot-swap)    │
  │                                                               │
  │   ┌──────────────┐  ┌──────────────┐  ┌──────────────┐        │
  │   │ mini-app     │  │ mini-app     │  │ mini-app     │        │
  │   │ Board (SDUI) │  │ Aria (webview│  │ time-X (SDUI)│  …     │
  │   │ .wasm        │  │  → SDUI dps) │  │ Go/Rust .wasm│        │
  │   └──────┬───────┘  └──────┬───────┘  └──────┬───────┘        │
  │          │ descritor SDUI + chamadas de capability (WIT)       │
  └──────────┼─────────────────┼─────────────────┼───────────────┘
             ▼                 ▼                 ▼
        controles nativos do SO (iOS/Android/Windows/Linux)
```

- **Shell = o host** dos ADRs anteriores, agora multi-módulo: gerencia o ciclo de vida
  de **vários** mini-apps (carregar sob demanda, descarregar, hot-swap — ADR 0003).
- **Cada mini-app** é um módulo WASM que emite seu descritor SDUI e faz chamadas de
  capability. Não conhece os outros mini-apps; fala só com o shell.
- **Serviços compartilhados** ficam no shell e são expostos aos mini-apps como
  capabilities (a auth, por exemplo, é uma capability `identity` que devolve tokens já
  obtidos pelo device-code/OBO — o mini-app nunca vê credencial).

### Mensageria entre mini-apps

Mini-apps não se enxergam diretamente (sandbox). Se o Board precisa abrir o Aria num
contexto, ele pede ao **shell** (`open-mini-app(id, params)`); o shell media. Isso
mantém o isolamento e dá ao shell um ponto único de auditoria/navegação.

## 4. Incorporando o Aria (caminho real)

O Aria já é web (`<rui-assessor>`). Dois caminhos, e o modelo **misto** é suportado:

1. **Rápido — mini-app webview:** o Aria entra como um mini-app cujo "render" é uma
   `WKWebView` (iOS) / `WebView2`/WebKitGTK (desktop) hospedada pelo shell, ao lado dos
   mini-apps SDUI-nativos. Reusa a web Aria **hoje**, com auth/capabilities/navegação do
   shell. É a ponte pragmática.
2. **Depois — migra pra SDUI-nativo:** reescreve a UI do Aria como emissor de descritor
   SDUI (ganha feel nativo, a11y, e sai da webview).

Um super-app **misto** (alguns mini-apps SDUI-nativos, outros webview) é normal e
esperado durante a transição. O webview aqui é um **mini-app**, não a arquitetura do
app — a tese "sem webview" vale pro Mabel-nativo; o webview é uma casca de
compatibilidade opcional por mini-app.

## 5. Registry e distribuição (resumo; detalhe em `docs/ota.md`)

- **Baked (AOT no build):** os mini-apps conhecidos entram compilados no binário PJUS.
  Zero download dinâmico → **100% compatível com App Store** (nada de "baixar código").
- **Dinâmico (interno/enterprise):** o registry serve `.wasm`+descritor sob demanda; o
  shell baixa e carrega. Livre em distribuição **interna/MDM**; na loja pública tem
  escrutínio (ver `docs/ota.md`).

## 6. Nota de policy (honesta)

Super-app com **download dinâmico de código** sofre escrutínio na App Store pública
(guideline 2.5.2). Para a PJUS isso **não é bloqueio**:

- **Interno / enterprise / MDM:** distribuição fora da loja pública → OTA de mini-apps
  livre.
- **Loja pública, se necessário:** com **AOT** os mini-apps ficam *baked* no build (sem
  download dinâmico) → compatível. Mini-apps novos entram numa atualização normal do app.

Ver `docs/ota.md` §policy para o detalhe interno-vs-público.

## 7. Escopo / não-metas

- **É:** o modelo de plataforma (shell multi-módulo, mini-apps sandbox, serviços
  compartilhados, registry, incorporação do Aria, framing organizacional PJUS).
- **Não é (ainda):** implementação do shell multi-módulo (hoje o host é single-módulo);
  protocolo de mensageria entre mini-apps; formato do registry; políticas de quota/
  recursos por mini-app; verificação/assinatura de mini-apps no registry.

## 8. Decisões pendentes (Daniel)

1. **Fronteira de sandbox entre mini-apps** — um WASM store/runtime por mini-app
   (isolamento máximo, mais RAM) vs. módulos no mesmo runtime com memórias separadas
   (mais leve). Recomendo um instance por mini-app pelo isolamento.
2. **Assinatura/verificação de mini-apps no registry** — quem pode publicar, como o
   shell confia. Precisa de design antes do OTA dinâmico.
3. **Aria: webview-mini-app já ou espera SDUI?** — recomendo webview-mini-app primeiro
   (reuso imediato) e migração pra SDUI como trilha paralela.
