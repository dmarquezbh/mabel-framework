# ADR 0005 — Mabel como plataforma de super-app (host + mini-apps WASM)

- **Status:** Proposto (enquadramento de plataforma; reusa ADRs 0001–0004)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-arch-consolidation`
- **Irmão de:** ADR 0001 (SDUI), 0002 (capabilities), 0003 (HMR), 0004 (desktop),
  0006 (OTA), 0007 (autoria poliglota). Design em `docs/super-app.md`.

## Contexto

O Mabel foi desenhado como framework de UI cross-platform (1 wasm poliglota + host fino
+ SDUI → controles nativos). O Daniel enquadrou o alvo real: o app **PJUS** é um
**super-app** que incorpora funcionalidades de vários times (Board, Aria, …). A
arquitetura já suporta isso naturalmente — falta nomear e documentar como pilar.

Necessidade organizacional: **cada time da PJUS entrega o seu mini-app; o super-app PJUS
renderiza todos.** Um app na loja; features crescem sem reinstalar; cada time no seu
stack; identidade/capabilities/storage/navegação compartilhados; isolamento entre
projetos.

## Decisão

O Mabel é uma **plataforma de super-app**, não só um framework de UI:

1. **Shell nativo = host multi-módulo.** O host dos ADRs anteriores passa a carregar e
   gerenciar **vários** módulos WASM (mini-apps), cada um emitindo seu descritor SDUI,
   todos renderizados pelos mesmos controles nativos.
2. **Mini-app = um módulo WASM sandbox por feature/time.** Isolado (memória linear
   própria), capability-gated pelo manifesto (ADR 0002), poliglota (ADR 0007). Não
   enxerga outros mini-apps; fala só com o shell.
3. **O shell provê serviços compartilhados:** identidade/auth (device-code/OBO — login
   uma vez), capabilities, storage (compartilhado + por-mini-app), navegação/launcher e
   mensageria mediada entre mini-apps.
4. **Registry de mini-apps:** o shell lista/carrega os publicados — **baked via AOT**
   (compatível com loja) ou **dinâmico interno** (enterprise/MDM; ADR 0006).
5. **Aria entra como mini-app webview** (reuso imediato da web existente) ao lado dos
   SDUI-nativos, migrando pra SDUI depois. Super-app **misto** é suportado. O webview é
   uma casca por-mini-app, não a arquitetura do app.

## Alternativas consideradas

- **Um app monolítico por feature (N apps na loja):** cada time publica seu próprio app.
  Rejeitada: N instalações, N logins, sem plataforma compartilhada, atualização lenta.
- **Super-app com mini-programs JS (modelo WeChat literal):** funciona, mas JS-only
  (não poliglota), isolamento por convenção de linguagem, e render em webview. WASM dá
  isolamento mais forte, poliglota, e controles nativos. Rejeitada em favor de WASM.
- **Não nomear como plataforma (deixar como framework de UI):** perderia o alinhamento
  organizacional (mini-app-por-time) que é a razão estratégica do projeto. Rejeitada.

## Consequências

- (+) Alinha a arquitetura à estratégia PJUS: um app, features por time, sem reinstalar.
- (+) Isolamento e segurança por-mini-app de graça (sandbox WASM + manifesto).
- (+) Democratiza: cada time no seu stack (ADR 0007), todos no mesmo descritor.
- (+) Auth/capabilities/storage compartilhados: login uma vez, UX coesa.
- (−) O host precisa virar **multi-módulo** (ciclo de vida de vários mini-apps) — hoje é
  single-módulo. Trabalho real.
- (−) Exige protocolo de mensageria entre mini-apps, formato de registry e
  assinatura/verificação — nada disso existe ainda.
- (−) Distribuição dinâmica na loja pública tem escrutínio (mitigado por AOT-baked ou
  distribuição interna — ADR 0006).

## Decisões pendentes (Daniel)

1. Fronteira de sandbox: um runtime/instance por mini-app (recomendado, isolamento) vs.
   módulos no mesmo runtime.
2. Assinatura/verificação de mini-apps no registry (pré-requisito do OTA dinâmico).
3. Aria: webview-mini-app já (recomendado) vs. esperar SDUI.
