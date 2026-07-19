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

- **AOT (baked):** compilar o wasm ahead-of-time e assá-lo no binário (no iOS,
  wasm2c→C→clang do xtool→arm64) dá **velocidade nativa**, mas o mini-app fica **preso no
  build** → **NÃO é OTA** (mudar exige rebuild + loja). **PROVADO no device sem Mac** (spike
  v2), **~163×** mais rápido que o interpretador num bench trivial.
- **Interpretador (WasmKit):** **PROVADO** rodando no iPhone via xtool **sem Mac**
  (interpretador puro-Swift, sem JIT). Carrega módulos **em runtime** → **habilita OTA**
  de mini-apps (nível 2). Custo: mais lento que AOT/JIT.
  > Achado do spike a registrar: **.NET→wasm não roda no WasmKit** (o .NET emite
  > WASI-preview2 Component + Mono ~3,34 MB; WasmKit é core-module + preview1 → rejeita;
  > core Rust ~55 B roda). Logo o **mini-app live-on-iOS é um lean core-wasm** (Rust/TinyGo/
  > AssemblyScript/C), não .NET (`NativeAOT-LLVM` seria o fix, mas bloqueado no WSL hoje).
  > .NET fica em autoria/build-time/desktop (ADR 0007). No desktop/Android, o runtime é mais
  > capaz (JIT) e a matriz de linguagens abre.

> **Os dois runtimes coexistem no MESMO app:** um app leva o core AOT-baked **e** o
> interpretador WasmKit ao mesmo tempo — mantém WASM rápido (baked) **e** tem OTA (lógica
> interpretada). Não é escolher um OU outro. **Limite honesto (física):** pra um mesmo pedaço
> de código, "velocidade-nativa + OTA-de-lógica-nova + App-Store-pública" não coexistem
> (rápido=baked=sem-OTA; OTA=interpretado=cinza-público). Interno PJUS não tem esse limite.

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

- **PJUS enterprise / interno / MDM:** distribuição fora da App Store pública (perfil
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

## 5. Store-safety: a linha DADO vs CÓDIGO (dois tiers)

A regra da Apple é simples: **dado é livre, código baixado não**. Isso reduz os 3 níveis
a **dois tiers de store-safety**:

### Tier 1 — SDUI puro (store-safe **e** instantâneo)

Host nativo + uma **biblioteca de componentes/ações BAKED** (no binário) + um **descritor
server-driven (DADO)**. O servidor manda o descritor (JSON/binário) → o app renderiza
controles nativos e interpreta **ações NOMEADAS que já conhece** (baked). **Zero código
baixado → zero 2.5.2.** Telas/layout/conteúdo novos = **OTA instantâneo, ilimitado, sem
review** (o modelo SDUI do Airbnb/Spotify). Ponto-chave: **pode nem precisar de WASM no
device** — se o vocabulário baked de ações/componentes for rico o bastante, quase todo
update é só descritor novo.

### Tier 2 — lógica portátil / mini-app com comportamento (WASM)

Quando a feature precisa de **comportamento genuinamente novo** além do vocabulário
baked, aí sim entra WASM:
- **AOT-baked (wasm2c→nativo):** revisado como binário nativo → **100% store-clean**,
  mas **sem OTA** (vai por release).
- **Interpretado (WasmKit):** **OTA** — livre interno, cinza público.

### Estratégia

**Investir num vocabulário rico de ações/componentes baked** → a maioria dos updates vira
**só descritor (dado)** = instantâneo e store-clean **pra sempre**. WASM-live fica
reservado ao comportamento novo. Ou seja: empurra o máximo de mudança pro Tier 1.

## 6. Modelo offline

- **WASM = o motor do offline.** Com WASM local, a lógica roda no device: gera o descritor
  a partir do estado local, trata evento e computa **offline**. Sem WASM (só
  server-driven), offline = apenas **cache read-only** (descritor + dados cacheados +
  ações baked nativas); lógica custom offline **não tem onde rodar**.
- **Híbrido (recomendado):** online = SDUI do servidor (fresco/instantâneo/OTA); cacheia
  descritor + dados **+ o módulo WASM**; offline = roda o WASM cacheado → app funcional de
  verdade; sincroniza ao voltar.
- **Simplificação:** **WASM AOT-baked = offline POR CONSTRUÇÃO** (está no binário, nem
  cacheia) + descritores do servidor pra frescor online por cima = melhor dos dois.
- **Regra:** app fino (só exibe dado do servidor) → dá pra dispensar WASM (cache + nativo,
  offline read-only). App offline-de-verdade (interativo/computa) → **mantém WASM** como
  motor local (baked recomendado).

## 7. Segurança do OTA

- **Descritor (nível 1)** não é código executável — o host só instancia controles
  nativos a partir dele; o pior caso é UI malformada, não execução arbitrária. Ainda
  assim: validar schema/versão e servir sobre canal autenticado.
- **Mini-app WASM (nível 2)** É código — exige **assinatura/verificação** no registry
  (quem publicou, integridade) antes do shell carregar. É pré-requisito do OTA dinâmico
  (ver ADR 0005 §pendências). O sandbox WASM + manifesto limitam o dano, mas não
  substituem verificação de origem.

## 8. Escopo / não-metas

- **É:** o modelo OTA em 3 níveis; os 2 tiers de store-safety; o modelo offline; a tensão
  AOT vs interpretador ancorada no spike; a matriz interno-vs-público com a leitura honesta
  da 2.5.2.
- **Não é (ainda):** implementação do canal de update; formato/assinatura do registry
  (ADR 0005); rollout gradual/rollback assinados; distribuição desktop (updater por-OS:
  MSIX/Squirrel, AppImage/Flatpak, Sparkle+notarização). (Nota: wasm2c-AOT **e** o
  interpretador WasmKit **ambos provados** no device na v2 do spike.)
