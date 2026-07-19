# ADR 0003 — HMR (hot module reload) e preservação de estado

- **Status:** Proposto (design; aguarda spike WASM-on-device + renderer SDUI)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-arch-consolidation`
- **Irmão de:** ADR 0001 (SDUI descriptor) e ADR 0002 (capabilities ABI). Design completo em `docs/hmr-e-estado.md`.

## Contexto

O `mabel dev` já observa arquivos, recompila o WASM e sinaliza o cliente por
WebSocket (`reload:<versão>`, ver `MabelDevServer`). Falta o comportamento do **host**
ao receber um WASM novo: como **trocar o módulo em runtime** (hot-swap) e, o ponto
difícil, **o que fazer com o estado do app** no swap.

Restrição intrínseca: um módulo WASM tem **memória linear própria**; ao instanciar
um módulo novo, a memória nasce zerada. Todo estado que vive *dentro* do guest se
perde no swap, salvo se for externalizado ou serializado/restaurado. Não há
continuação gratuita.

Restrições herdadas do stack:
- **Guest poliglota** (C#/Blazor, Go/TinyGo, Rust) → a solução não pode depender de
  um runtime de linguagem específico.
- **Runtime:** desktop com JIT (wasmtime) — swap barato; iOS dev com WasmKit
  interpretado — swap possível, sem JIT; iOS release com wasm2c AOT — **sem** HMR.
- **SDUI (ADR 0001):** o descritor já é uma função pura do estado; re-render é
  reconciliação de árvore → controles nativos.
- **Capabilities (ADR 0002):** há uma tabela `reqId → continuation` e handles nativos
  vivos (câmera/GPS/socket) que **não** podem sobreviver a um swap.

## Decisão

Estratégia **em camadas**, escolhida em runtime pelo host:

1. **Padrão arquitetural — (c) estado externalizado num store do host.** O app é
   escrito como `view(state) → descritor SDUI` + `update(state, action) → state`
   (estilo Elm/TEA). O estado vive num store cuja lifetime é do host; no hot-swap o
   host mantém o store, instancia o módulo novo e chama `view(state)`. Sobrevive por
   construção. É a **única** opção que compõe com hot-swap **e** guests poliglotas, e
   já casa com o SDUI.
2. **Transporte — (b) snapshot.** Exports `mabel_serialize_state()`/`mabel_restore_state()`
   movem o blob de estado (opaco pro host) através do swap. É *como* (c) se implementa
   quando o guest é dono da (de)serialização.
3. **Otimização .NET — (d) Roslyn Hot Reload / metadata-update.** Para edições de
   corpo de método no guest .NET rodando no runtime interpretado (mono-wasm), aplica
   deltas de IL **sem swap** → estado 100% preservado, loop mais rápido. "Rude edits"
   caem pro swap. Não existe no caminho AOT (release não faz HMR de qualquer modo).
4. **Fallback — (a) reload total.** Quando o shape do estado mudou incompatível, a
   restauração falhou, ou o dev pediu reset.

No swap, o host **encerra** handles nativos vivos e **drena** a tabela
`reqId→continuation` (cancela chamadas de capability em voo); o módulo novo
**re-subscreve** o que ainda fizer sentido. Estado que sobrevive = dado puro no store
(tela/navegação/form/scroll/dados); ligações vivas com o SO **não** sobrevivem.

## Alternativas consideradas

- **(a) sozinha (reload total):** simples e sempre correta, mas mata a ergonomia do
  caso comum. Rebaixada a fallback.
- **(b) sozinha (snapshot):** funciona, mas empurra (de)serialização + migração de
  shape pro dev em toda edição; sem uma disciplina de estado, vira boilerplate frágil.
  Mantida como transporte de (c).
- **(d) sozinha (Hot Reload):** padrão-ouro de preservação, mas **só .NET**, só
  edições não-rude, e (provavelmente) só onde o mono-wasm interpretado roda. Não
  serve guest poliglota nem rude edits. Mantida como otimização.
- **Futures/Component Model pra estado:** fora de alcance pelo mesmo motivo do ADR
  0002 (Component Model imaturo no stack). Não aplicável.

## Consequências

- (+) HMR real no desktop (rápido, JIT) e no iPhone (WasmKit interpretado), reusando
  o `MabelDevServer` que já existe.
- (+) Preservação de estado robusta e **honesta**: dado puro sobrevive; recursos vivos
  são religados, não fingidos.
- (+) O padrão (c) melhora testes e habilita time-travel debugging de brinde.
- (−) Impõe (ou fortemente recomenda) um **modelo de programação** view/update ao app.
- (−) O host precisa custodiar o blob de estado e gerenciar cleanup de handles/
  continuations no swap (disciplina, senão vaza).
- (−) Migração automática de shape de estado fica best-effort (v1); mudança
  incompatível cai no reload total.

## A validar / decidir (Daniel ou spike)

1. **Roslyn metadata-update sob WasmKit (iOS)** — aplica deltas no mono-wasm
   interpretado dentro do WasmKit? Se não, (d) fica só desktop; iOS usa sempre
   swap+(c)/(b). **Spike WASM-on-device.**
2. **Formato do blob** — JSON (v1, simples) vs binário. Recomendo JSON.
3. **Modelo obrigatório vs recomendado** — o Mabel *impõe* view/update ou só
   recomenda (deixando (b) puro disponível)? Decisão de produto do Daniel.
