# Mabel — Autoria poliglota (como cada linguagem escreve componentes)

> **Pilar.** Como cada stack produz um mini-app. Irmão do ADR 0005 (super-app);
> decisão em `docs/adr/0007-autoria-poliglota.md`.

## 1. O ponto central

> **O contrato é o descritor SDUI + o WIT de capabilities — NÃO o Blazor.**

Blazor é apenas o **jeito idiomático do C#** de produzir o descritor. Qualquer linguagem
que compile pra WASM e saiba emitir a árvore SDUI (e falar o WIT) é um cidadão de
primeira classe. Isso é o que torna a plataforma **poliglota por time** (ADR 0005): cada
time da PJUS escreve no seu stack; todos convergem no mesmo descritor.

## 2. As três camadas

```
  ┌──────────────────────────────────────────────────────────────┐
  │  FONTE ÚNICA = WIT / schema                                   │
  │  (descritor SDUI + interfaces de capability, mabel:*)          │
  └───────────────────────────┬──────────────────────────────────┘
                              │  wit-bindgen / codegen
        ┌─────────────────────┼─────────────────────┐
        ▼                     ▼                     ▼
   tipos C#              tipos Go              tipos Rust
   + bindings cap        + bindings cap        + bindings cap     ← GERADOS (grátis)
        │                     │                     │
        ▼                     ▼                     ▼
   AÇÚCAR DE AUTORIA idiomático por linguagem (bespoke por lang):
   Blazor/Razor +        builder/funcs         macros/RSX ou
   renderer custom       (VStack(Card(...)))   Dioxus/Leptos
        │                     │                     │
        └─────────────────────┴─────────────────────┘
                              ▼
                    módulo .wasm (mini-app)
                    emite descritor SDUI + chama capabilities
```

### Camada 1 — Fonte única: WIT/schema

O descritor SDUI e as interfaces de capability são descritos **uma vez** em WIT
(`package mabel:*`). É a verdade do contrato (ADR 0001, 0002).

### Camada 2 — Codegen: tipos por linguagem (grátis)

**wit-bindgen / codegen** gera, a partir do WIT, os **tipos do descritor** e os
**bindings de capability** para cada linguagem (C#, Go, Rust). O grosso do trabalho de
"falar o protocolo" em cada lang é **gerado**, não escrito à mão. Trocar o WIT
re-gera todos.

### Camada 3 — Açúcar de autoria idiomático (bespoke por linguagem)

Sobre os tipos gerados, cada linguagem ganha uma camada ergonômica no seu estilo:

- **C# (flagship):** **Blazor/Razor** + **renderer custom** que emite o descritor SDUI
  (não DOM). Melhor DX, hot reload (ADR 0003 no caminho .NET). Referência de
  implementação: **fork do BlazorBindings** (retarget do backend MAUI→SDUI) —
  **NÃO** o MAUI (que exige Mac).
- **Go:** funções/builder idiomáticos (`VStack(Card(...), Card(...))`) ou uma lib
  reativa mínima estilo **templ/gomponents**. Compila via **TinyGo→wasm**.
- **Rust:** **macros/RSX** ou adaptar **Dioxus/Leptos** (já produzem uma árvore virtual
  — casa direto com o descritor). Compila **Rust→wasm**.

## 3. SDK-guest fino por linguagem + core compartilhado

Cada linguagem tem um **SDK-guest fino**:

- **tipos + bindings de capability** (codegen — grátis),
- **o loop de render** (estado → descreve → evento; casa com o store externalizado do
  ADR 0003),
- **o açúcar de autoria** (camada 3).

O **core continua compartilhado e único**: host, renderer (descritor → controles
nativos), implementações de capability, shell do super-app. Nada disso é por-linguagem.
Ou seja: o custo poliglota fica **contido no SDK-guest fino**; a máquina pesada é comum.

## 4. Interação com a realidade on-device (honesto)

O que **roda como guest live no device** depende do runtime da plataforma (ver ADR 0006
e o achado do spike):

- **iOS live (WasmKit, core-module + preview1):** **lean core-wasm** — Rust, TinyGo,
  AssemblyScript, C. **.NET→wasm NÃO roda** no WasmKit (emite preview2/Mono). Então o
  **mini-app live-on-iOS é lean-lang**, não .NET.
- **.NET/C#/Blazor:** brilha em **autoria**, **geração de descritor em build-time**
  (ex.: `board_gen` roda no build/WSL e emite JSON de descritor — a tela iOS provada
  hoje é assim) e no **desktop** (runtime wasm com JIT roda .NET-wasm).
- **Desktop / Android (JIT):** runtime mais capaz → a matriz de linguagens abre (incl.
  .NET). Ver a matriz de status no README.

Portanto "poliglota" tem uma nuance honesta: **o contrato é poliglota sempre**; **quais
linguagens rodam como guest live depende da plataforma/runtime**. C# é flagship na
autoria e no desktop/build-time; lean-langs são o guest live no iOS.

## 5. Prioridade (honesto: arquitetura PERMITE, não obriga os três já)

- **C#/Blazor primeiro** — é o time Opera, é o flagship de autoria, e o caminho com
  melhor DX. Prova a plataforma.
- **Go/Rust habilitados publicando o WIT + o gerador** — a comunidade/os times podem
  entrar depois; a plataforma **permite**, mas não exige os três SDKs prontos no dia 1.
- Mantém-se **um SDK-guest por linguagem** (trabalho real), mas o **codegen cobre o
  grosso** e o core é compartilhado — o custo marginal de mais uma linguagem é o açúcar
  de autoria, não a máquina inteira.

## 6. Escopo / não-metas

- **É:** o modelo de autoria em 3 camadas (WIT único → codegen → açúcar por lang), o
  SDK-guest fino + core compartilhado, a nuance on-device, a priorização C#-primeiro.
- **Não é (ainda):** o pipeline wit-bindgen real pra C#/Go/Rust; o renderer Blazor
  custom (fork do BlazorBindings) implementado; os SDKs Go/Rust; benchmark de DX entre
  as camadas de açúcar.
