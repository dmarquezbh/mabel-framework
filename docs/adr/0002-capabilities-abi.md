# ADR 0002 — ABI de Capabilities do Mabel (acesso a APIs nativas via host)

- **Status:** Proposto (design de fase 2; aguarda spike WASM-on-device + renderer SDUI provarem o caminho)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-capabilities-abi`
- **Irmão de:** ADR 0001 (SDUI descriptor). O descritor SDUI é *o que desenhar*; esta ABI é *o que o app pode fazer no device*.

## Contexto

O app Mabel roda como **WASM sandbox** (.NET → wasm, executado por WasmKit dentro do
shell Swift no device). Sandbox = zero acesso direto ao SO; o guest só fala com o host
por **imports/exports** (o canal de render já é assim: `Protocol.cs` ↔ `WasiContract.cs`).

Para o app ser útil (não só desenhar), ele precisa de **APIs nativas**: câmera, GPS,
notificações, biometria, secure-storage, share, clipboard, haptics. Três decisões de
design precisam ser tomadas antes de implementar:

1. **Formato do contrato** — WIT/Component Model idiomático, ou funções core-module à mão?
2. **Assincronia** — WASM é síncrono; APIs nativas são async. Como pontear?
3. **Segurança** — como limitar o que cada app pode acessar?

Constraints herdadas (do stack já decidido):

- **Guest = .NET → WASI Preview 1 core module** (workload `wasi-experimental`). O .NET
  ainda **não** emite componentes do Component Model de primeira classe; `componentize-dotnet`
  (NativeAOT-LLVM + wit-bindgen) existe mas é caminho separado e imaturo pra este uso.
- **Host = WasmKit (Swift).** Runtime WASM em Swift puro; roda core module + WASI p1 bem,
  mas suporte a **Component Model / WASI Preview 2 (futures, streams, `wasi:io/poll`)**
  é experimental/incompleto.
- **Build iOS sem Mac, via xtool** (mesma trave do ADR 0001). Nenhum passo pode depender
  de Xcode/Mac.
- **Conta Apple FREE** (`dmarquesbh@gmail.com`, Personal Team): sem Push, sem App Groups,
  sem Associated Domains, sem iCloud/iCloud Keychain, perfis de 7 dias.

## Decisão

### D1 — WIT como contrato semântico; core-module achatado como wire

Escrevemos as interfaces em **WIT** (`Capabilities/wit/*.wit`, `package mabel:capabilities`)
porque é a linguagem certa pra descrever capabilities tipadas e é o **alvo de migração**
natural. **Mas o transporte real hoje é core-module WASI p1** — funções achatadas com
nomes fixos (`cap_camera_capture`, `cap_perm_check`…) em `CapabilityContract.cs`, com
strings/records passando como `(ptr,len)` em memória linear (JSON/UTF-8), exatamente como
o `draw_text` já existente.

**Por quê:** o Component Model exigiria bindgen p2 nas duas pontas (componentize-dotnet +
WasmKit-componentes) — nenhum sólido agora. Duplicar o padrão já provado do render
(`Protocol.cs`↔`WasiContract.cs`) é baixo risco e imediatamente implementável. O WIT não
fica só decorativo: é a spec revisável e o north-star; trocar o lowering depois não muda
o modelo.

### D2 — Assincronia por request-id + callback (não futures)

- Toda operação async recebe um `request-id: u64` gerado pelo guest e **retorna na hora**
  um `CapStatus` (aceito / negado localmente).
- O trabalho nativo roda async no host; ao terminar, o host chama **um único export do
  guest**, `mabel_on_capability_result(request-id, capability, status, ptr, len)`.
- O guest despacha por `(request-id, capability)` e completa a continuation
  (`TaskCompletionSource`), dando `await` idiomático pro dev do app.
- **Streams** (GPS contínuo) = múltiplos callbacks com o mesmo `request-id` até `stop-updates`.
- **Memória do payload:** host chama o export `cap_alloc(len)` do guest, escreve os bytes,
  passa `(ptr,len)`; guest lê e chama `cap_free`.

**Por quê, e não `wasi:io/poll`/futures do Component Model:** futures seriam mais limpos,
mas dependem de Component Model + p2 nas duas pontas (ver constraints). O callback único
é o mínimo que funciona sobre core-module p1 hoje, é trivial de implementar em Swift/WasmKit,
e mapeia direto pro async/await do .NET no guest. **Custo aceito:** o guest carrega uma
tabela `request-id → continuation` à mão (o bindgen faria isso sozinho no mundo p2).

### D3 — Segurança capability-based em duas camadas

1. **Manifesto (atenuação host-side):** o host não liga nenhuma API por padrão; lê o
   `CapabilityManifest` (JSON no bundle) no load e só provê o import real das capabilities
   **declaradas**. Não declarada → stub que devolve `NotAuthorized`; o guest nunca alcança
   o SO. Least authority por construção.
2. **Consentimento do SO (runtime):** câmera/GPS/notif/biometria ainda exigem o prompt
   nativo do iOS. Exposto pela interface `permissions` (check síncrono + request async);
   qualquer capability pode voltar `permission-denied`.

O manifesto é também a **fonte única das usage-strings** do Info.plist (o passo de build
xtool injeta), evitando o crash clássico do iOS por permissão pedida sem usage-string.

### D4 — Recorte pela conta free

Como a conta é FREE, a ABI v2 **expõe só o que passa sem App ID no portal**:
`notifications` = **só local** (push cortado), `secure-storage` = **só por-app**
(sem groups/iCloud). Câmera, galeria, GPS (when-in-use), biometria, share, clipboard e
haptics passam com usage-string no Info.plist. Tabela completa em `docs/capabilities-abi.md §5`.

## Consequências

- (+) Implementável **já** sobre o stack atual (p1 + WasmKit), reusando o padrão de wire
  do render. Sem esperar Component Model amadurecer.
- (+) Segurança auditável e por-construção (manifesto), alinhada ao modelo WASI de capabilities.
- (+) WIT tipado serve de spec e de rota de migração pra p2 sem redesenhar o modelo.
- (−) O guest gerencia `request-id → continuation` e memória de payload à mão (boilerplate
  que o bindgen p2 eliminaria).
- (−) Um único export de callback concentra o roteamento; precisa de disciplina pra não
  vazar continuations (timeout + limpeza).
- (−) Recorte pela conta free deixa push e keychain-compartilhado fora do v2; uma conta
  paga no futuro reabre essas trilhas (só somar entries no manifesto + entitlements).

## Decisões que precisam do Daniel (ou do spike) antes de implementar

1. **Async: callback vs. futures** — recomendo **callback** (D2) pelas constraints. Se o
   spike mostrar Component Model viável no WasmKit + componentize-dotnet, reabrir.
2. **Serialização do payload** — este design usa **JSON** em memória linear (simples,
   igual ao SDUI v1). Um formato binário flat (estilo `RenderCommand`) seria mais rápido
   pra payloads grandes (foto) — mas foto já vai por `read-asset` em chunks, então JSON
   pros metadados basta. Confirmar.
3. **Injeção de Info.plist/entitlements pelo xtool** — confirmar a sintaxe real no
   `xtool.yml` do repo (o hello-world já gera Info.plist). É o que valida D4 na prática.
4. **`camera` vs `photo-library` como capabilities separadas** — hoje são ids distintos
   (usage-strings distintas no iOS). Mantido separado; confirmar se o app quer as duas.
```
