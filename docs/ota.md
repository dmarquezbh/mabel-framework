# Mabel — Atualização em runtime / OTA

> **Pilar de plataforma.** Trunfo-chave do super-app: entregar features sem passar pela
> loja. Irmão do ADR 0005 (super-app); decisão em `docs/adr/0006-ota.md`.

## 1. A ideia

No super-app (ADR 0005), o **shell nativo fica parado** (raramente muda) e os
**mini-apps — wasm + descritor — são conteúdo**. Trocar/adicionar um mini-app é trocar
um arquivo, não republicar o app. Isso permite **OTA (over-the-air)**: features crescem
e são corrigidas **sem reinstalar** e (dependendo do nível) **sem passar pela loja**. É
o modelo do WeChat, aplicado com WASM.

## 2. Três níveis de atualização

| Nível | O que muda | Contém lógica nova? | OTA interno | App Store pública |
|---|---|---|---|---|
| **1. Descritor-only** | UI/conteúdo (a árvore SDUI, textos, layout, dados) | Não — dado puro | ✅ sempre seguro | ✅ sem problema (é dado, não código) |
| **2. Mini-app WASM (lógica)** | um `.wasm` novo/atualizado, rodado pelo **interpretador** | Sim (lógica executável) | ✅ livre | ⚠️ cinza (ver §4) |
| **3. Shell nativo** | o host/app nativo em si | Sim (código nativo) | ❌ só loja | ❌ só loja |

- **Nível 1 (descritor-only)** é o mais poderoso no dia a dia: mudar telas, textos,
  ordem, dados exibidos = mandar um JSON de descritor novo. É **dado puro**, zero
  policy, sempre OTA, e trivialmente seguro (o host só renderiza controles nativos a
  partir dele — não executa código do descritor).
- **Nível 2 (mini-app WASM)** entrega **lógica** nova (um mini-app inteiro, ou uma
  versão nova). Exige um **runtime que carregue módulo em runtime** — o **interpretador**
  (WasmKit no iOS; runtime JIT no desktop/Android). É a diferença crucial com AOT (§3).
- **Nível 3 (shell)** é raro (o shell é fino e estável); quando muda, é atualização
  normal de app pela loja.

## 3. A tensão AOT-baked vs. interpretador-OTA (explícita)

Há um trade-off **real** entre velocidade e atualizabilidade, ancorado no que o spike
WASM-on-device provou:

- **AOT (baked):** compilar o wasm ahead-of-time e assá-lo no binário (no iOS, o caminho
  aspiracional wasm2c→C→arm64) dá **velocidade nativa**, mas o mini-app fica **preso no
  build** → **NÃO é OTA** (mudar exige rebuild + loja).
- **Interpretador (WasmKit):** **PROVADO** rodando no iPhone via xtool **sem Mac**
  (interpretador puro-Swift, sem JIT). Carrega módulos **em runtime** → **habilita OTA**
  de mini-apps (nível 2). Custo: mais lento que AOT/JIT.
  > Achado do spike a registrar: **.NET→wasm não roda no WasmKit** (o .NET emite
  > WASI-preview2 Component + Mono; WasmKit é core-module + preview1 → rejeita). Logo o
  > **mini-app live-on-iOS é um lean core-wasm** (Rust/TinyGo/AssemblyScript/C), não
  > .NET. .NET fica em autoria/build-time/desktop (ADR 0007). No desktop/Android, o
  > runtime é mais capaz (JIT) e a matriz de linguagens abre.

### Estratégia recomendada (combina os três)

1. **Core AOT** — o shell e os mini-apps críticos/estáveis ficam AOT-baked (rápidos, via
   loja).
2. **Mini-apps novos / updates via interpretador OTA** — entregues sem loja em
   distribuição **interna** (WasmKit no iOS; JIT no desktop/Android).
3. **Descritor-OTA sempre** — o loop mais rápido e seguro pra mudança de UI/conteúdo,
   em qualquer distribuição, inclusive loja pública.

Ou seja: **rápido onde precisa (AOT), atualizável onde importa (interpretador),
instantâneo pro grosso das mudanças (descritor).**

## 4. Interno vs. público (policy, honesto)

- **Org enterprise / interno / MDM:** distribuição fora da App Store pública (perfil
  enterprise/MDM). **OTA livre** — não passa por App Review. Níveis 1 e 2 liberados.
- **App Store pública:** a guideline **2.5.2** restringe **baixar e executar código**
  não incluído no binário. Nuances:
  - **JavaScript tem carve-out** explícito (JSContext/WKWebView) — é por isso que
    React Native/CodePush/WeChat podem entregar JS OTA.
  - **WASM rodado pelo teu próprio interpretador NÃO tem bênção explícita** da Apple →
    é **zona cinza**. Não assumir que passa.
  - **Saídas públicas seguras:** (a) **descritor-OTA** (nível 1 — dado, não código; ok);
    (b) **mini-app webview** (o JS do webview cai no carve-out); (c) **mini-apps
    AOT-baked** (sem download dinâmico — 100% compatível, mas sem OTA de lógica).

Resumo: **interno = OTA de tudo; público = descritor-OTA + webview + AOT-baked, e o
OTA de lógica-WASM fica cinza (evitar depender dele na loja pública).**

## 5. Segurança do OTA

- **Descritor (nível 1)** não é código executável — o host só instancia controles
  nativos a partir dele; o pior caso é UI malformada, não execução arbitrária. Ainda
  assim: validar schema/versão e servir sobre canal autenticado.
- **Mini-app WASM (nível 2)** É código — exige **assinatura/verificação** no registry
  (quem publicou, integridade) antes do shell carregar. É pré-requisito do OTA dinâmico
  (ver ADR 0005 §pendências). O sandbox WASM + manifesto limitam o dano, mas não
  substituem verificação de origem.

## 6. Escopo / não-metas

- **É:** o modelo OTA em 3 níveis; a tensão AOT vs interpretador ancorada no spike; a
  matriz interno-vs-público com a leitura honesta da 2.5.2.
- **Não é (ainda):** implementação do canal de update; formato/assinatura do registry
  (ADR 0005); rollout gradual/rollback; wasm2c-AOT (aspiracional, não provado — só o
  interpretador WasmKit está provado no device).
