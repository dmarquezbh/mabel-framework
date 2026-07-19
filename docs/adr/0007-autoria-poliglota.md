# ADR 0007 — Autoria poliglota (WIT como fonte única → codegen → açúcar por linguagem)

- **Status:** Proposto (design de plataforma)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-arch-consolidation`
- **Irmão de:** ADR 0001 (SDUI), 0002 (capabilities), 0005 (super-app), 0006 (OTA).
  Design em `docs/autoria-poliglota.md`.

## Contexto

A plataforma de super-app (ADR 0005) promete **poliglota por time**: cada time da Org
no seu stack, todos emitindo o mesmo descritor SDUI. Isso exige decidir **como cada
linguagem escreve componentes** sem duplicar a máquina inteira por linguagem, e sem
amarrar a plataforma ao .NET/Blazor.

Ponto central: **o contrato é o descritor SDUI + o WIT — não o Blazor.** Blazor é o jeito
idiomático do C#. Restrição factual (spike, ADR 0006): **.NET→wasm não roda no WasmKit
(iOS)**; o guest live-on-iOS é lean core-wasm; .NET brilha em autoria/build-time/desktop.

## Decisão

Autoria em **três camadas**:

1. **Fonte única = WIT/schema.** Descritor SDUI + interfaces de capability descritos uma
   vez (`package mabel:*`).
2. **Codegen (wit-bindgen) gera tipos + bindings por linguagem** (C#, Go, Rust) — o
   grosso de "falar o protocolo" é **gerado**, não escrito à mão.
3. **Açúcar de autoria idiomático por linguagem** (bespoke, sobre os tipos gerados):
   - **C# (flagship):** Blazor/Razor + renderer custom → descritor (fork do
     **BlazorBindings** como referência, retarget MAUI→SDUI; **não** MAUI).
   - **Go:** builder/funcs idiomáticos ou lib estilo templ/gomponents; TinyGo→wasm.
   - **Rust:** macros/RSX ou adaptar Dioxus/Leptos; Rust→wasm.

**SDK-guest fino por linguagem** = tipos+bindings (codegen) + loop de render (estado→
descreve→evento, casa com o store do ADR 0003) + açúcar. **Core compartilhado**
(host/renderer/capabilities/shell) — não é por-linguagem.

**Nuance on-device (honesta):** o contrato é poliglota sempre; **quais linguagens rodam
como guest live depende do runtime da plataforma** — iOS(WasmKit)=lean-langs;
desktop/Android(JIT)=abre incl. .NET; C# flagship em autoria/build-time/desktop.

**Prioridade:** **C#/Blazor primeiro** (time Ledger, melhor DX, prova a plataforma);
Go/Rust **habilitados** publicando o WIT + o gerador (comunidade/depois). A arquitetura
**permite** os três; não **obriga** os três prontos no dia 1.

## Alternativas consideradas

- **Blazor como o contrato (só C#):** amarraria a plataforma ao .NET e quebraria a tese
  poliglota do super-app. Rejeitada — Blazor é uma camada de autoria, não o contrato.
- **Um DSL novo próprio pra UI:** reinventa o que Razor/RSX/builders já fazem bem por
  linguagem, e força todo mundo a aprendê-lo. Rejeitada — reusar o idioma de cada lang.
- **Bindings à mão por linguagem (sem codegen):** boilerplate e drift entre langs quando
  o contrato muda. Rejeitada em favor de wit-bindgen (fonte única → regenera todos).
- **Três SDKs completos já:** custo alto sem necessidade imediata. Rejeitada — C#
  primeiro, os outros habilitados por contrato.

## Consequências

- (+) Democratiza: cada time no seu stack, todos no mesmo descritor.
- (+) Custo poliglota **contido no SDK-guest fino**; a máquina pesada (core) é comum.
- (+) Fonte única (WIT) elimina drift — mudou o contrato, regenera todos os tipos.
- (−) Ainda é **um SDK-guest por linguagem** (loop + açúcar) a manter, mesmo com codegen.
- (−) O renderer Blazor custom (fork do BlazorBindings) é trabalho não-trivial e ainda
  não existe.
- (−) Nuance on-device: quem quiser mini-app **live-on-iOS** hoje usa lean-lang, não
  .NET — a promessa poliglota tem asterisco por plataforma/runtime.

## Pendências (Daniel)

1. Ordem dos SDKs além do C#: Go ou Rust primeiro (se e quando).
2. Pipeline wit-bindgen real para C# (o .NET ainda não emite Component Model de 1ª
   classe — confirmar o gerador de tipos que será usado).
3. Confirmar o fork do BlazorBindings como base do renderer custom C#.
