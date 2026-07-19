# ADR 0008 — Debugging & DevTools (modelo multi-camada)

- **Status:** Proposto (design; implementação = onda 4 do roadmap, task #20)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-arch-consolidation`
- **Irmão de:** ADR 0001 (SDUI), 0002 (capabilities), 0003 (HMR), 0005 (super-app).
  Design em `docs/debugging.md`.

## Contexto

Um dev perguntou como se debuga um app Mabel. Hoje a resposta real é primitiva: **`NSLog`
via `idevicesyslog`** (foi como o tap no device foi validado). Precisamos do modelo maduro,
e ele é **multi-camada** porque o app tem fronteiras distintas: a lógica (guest WASM), o
descritor (árvore SDUI), o render nativo, e o wire guest↔host.

## Decisão

Debug em **quatro camadas**, cada uma com sua ferramenta:

1. **Lógica (guest):** debuga no **desktop/build-host** (runtime full, debugger da
   linguagem) — a lógica é a mesma do device; on-device interpretado tem trace limitado.
2. **Descritor:** **inspector de descritor** (árvore/props ao vivo/diff/time-travel) —
   estilo React DevTools / Flutter inspector; trivial porque descritor é dado puro.
3. **Render nativo:** **select-mode** (toca view nativa → nó SDUI de origem pelo `Id`).
4. **Wire:** **wire inspector** ("aba Network" do protocolo: descritores, eventos,
   capabilities com `reqId`/streams).

**Alavancas Mabel-específicas:**
- **Web-host + DevTools do browser = superfície primária de debug** (mesmo descritor no
  web e no nativo via HMR multi-alvo → debuga no Chrome DevTools, fiel ao nativo, reusa
  tooling maduro).
- **Replay determinístico** (app = descritor + WASM + estado externalizado → captura e
  re-executa → repro a partir do dado).
- **Error boundaries** (erro de nó/guest isola no mini-app/subárvore, não derruba o
  super-app; overlay no dev).

**Produção:** logging estruturado (evolução do `NSLog` atual) + New Relic + captura remota
de descritor+estado.

## Alternativas consideradas

- **Só logs (status quo):** suficiente pra prova, insuficiente pra escala. Mantido como
  degrau, não destino.
- **Um único debugger monolítico:** não casa com as 4 fronteiras distintas (linguagem ≠
  árvore ≠ render ≠ wire). Rejeitado em favor de ferramenta-por-camada.
- **Debugar só no device:** interpretado on-device tem debug limitado e é lento; o
  web-host + desktop dão fidelidade + tooling maduro. Device fica pra validação final.

## Consequências

- (+) DX competitiva com Flutter/RN reusando DevTools do browser (não reconstruir do zero).
- (+) Replay determinístico e error boundaries caem da arquitetura (descritor+estado+sandbox).
- (−) Tudo isto é **onda 4** — hoje só há `NSLog`. Depende do host multi-módulo, do host
  web e da integração WASM-live existirem.
- (−) Inspector/wire/select-mode/replay são ferramentas reais a construir (trabalho 🟢).

## Pendências

1. Formato do protocolo de inspeção (o wire inspector precisa de um canal de debug no
   dev-server, além do `reload:<versão>`).
2. Onde roda o inspector (extensão de browser reusando DevTools? app separado?).
