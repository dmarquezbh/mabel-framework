# ADR 0003 — Padrão de STREAM / subscription na ABI de capabilities

- **Status:** Proposto (design de fase 2; estende o ADR 0002)
- **Data:** 2026-07-19
- **Contexto do repo:** `github.com/dmarquezbh/mabel-framework`, branch `feat/mabel-capabilities-abi`
- **Estende:** ADR 0002 (ABI de capabilities). O 0002 resolveu o async **one-shot**;
  este resolve o async **contínuo** (eventos ao longo do tempo).

## Contexto

O ADR 0002 fixou o async **one-shot**: o guest chama `import(request-id, …)`, o host
trabalha e devolve **um** resultado no export `on-capability-result`. Isso cobre "tira uma
foto", "pega a posição agora", "autentica com Face ID".

Ao desenhar a capability **bluetooth (BLE)**, a lacuna ficou óbvia: várias operações são
**fluxos de N eventos ao longo do tempo**, não uma resposta única —

- **scan BLE:** cada advertising packet é um evento (o mesmo device reaparece com RSSI vivo);
- **notify/indicate de característica:** o peripheral empurra novos valores quando quiser;
- **estado de conexão:** quedas involuntárias chegam a qualquer momento.

E não é só BLE — **GPS contínuo** (o `start-updates` ad-hoc que o 0002 já tinha gambiarrado
reusando o request-id), **notificações recebidas/tocadas**, e no futuro **sensores** (aceler./
giroscópio) e **áudio** (frames). O one-shot não modela nada disso de forma limpa.

## Decisão

### D1 — Padrão subscription: `subscribe → N × on-capability-event → unsubscribe`

Um **segundo** mecanismo async, irmão do one-shot, coexistindo com ele:

- O guest gera um **`subscription-id: u64`** (espaço de id **separado** do `request-id`) e
  chama a função `subscribe-*` específica da capability (params tipados: filtro de scan,
  acurácia de GPS, UUID de característica). Retorna já um `cap-status` (aceite/negação).
- O host empurra os eventos no **segundo export único** do guest,
  `on-capability-event(subscription-id, capability, event-kind, payload)`, N vezes.
  O guest despacha por `subscription-id` (tabela → `IObservable`/`Channel<T>`).
- **`event-kind: u32`** discrimina o tipo de evento **dentro** da capability (BLE:
  0=device-found, 1=characteristic-changed, 2=connection-changed). Semântica por capability.
- **Cancelamento genérico:** uma função só, `streaming.unsubscribe(subscription-id)`, derruba
  qualquer stream; o host mapeia `sub-id → capability + recurso nativo` e faz o teardown
  (para o scanner, desliga a notificação, encerra o location manager…).

Wire achatado (core-module p1), irmão do one-shot — ver `CapabilityContract.cs`:
`mabel_on_capability_event` (export) + `cap_unsubscribe` (import) + `cap_*_subscribe_*`
(imports por capability). Memória do payload = mesmo protocolo `cap_alloc`/`cap_free`.

### D2 — Coexiste com o one-shot; uma capability pode ter os dois

Não substitui o 0002. Uma capability escolhe o padrão certo por operação — e **bluetooth
usa os dois** (o que faz dele a prova do design): `connect`/`discover`/`read`/`write` são
one-shot; `start-scan`/`subscribe-characteristic`/`subscribe-connection` são stream.
`location` ganhou `subscribe-updates` (stream) ao lado de `get-current` (one-shot),
aposentando o `start-updates`/`stop-updates` ad-hoc do 0002.

### D3 — Não usar streams/futures do Component Model (ainda)

Mesma razão do 0002-D2: `wasi:io/streams` + `wasi:io/poll` + futures exigem Component Model
+ WASI Preview 2 nas duas pontas (componentize-dotnet no guest, suporte a componentes no
host WASM), que o stack não tem sólido hoje (WasmKit interpretado no iOS; JIT no Android/
desktop, mas ainda sem CM maduro em .NET). O callback de stream é a via achatada sobre
core-module p1; a semântica (WIT) já é a de streams, então migrar depois troca só o lowering.

## Consequências

- (+) Modela eventos contínuos de primeira classe; BLE, GPS-contínuo e notificações-recebidas
  deixam de ser gambiarra. Extensível: sensores/áudio entram sem mudar a ABI (só novos
  `subscribe-*` + event-kinds).
- (+) Um só export de evento + um só `unsubscribe` = wire mínimo, simétrico ao one-shot.
- (+) Platform-neutral: iOS (delegate callbacks), Android (listeners/callbacks), desktop
  (event handlers) implementam o mesmo contrato.
- (−) O guest agora mantém **duas** tabelas de despacho (request-id → continuation;
  subscription-id → canal) e precisa de disciplina de teardown (todo `subscribe` casa com um
  `unsubscribe`, senão vaza recurso nativo — bateria no scan BLE!). O bindgen p2 automatizaria.
- (−) Sem backpressure nativo do p1: se o guest for lento, os eventos enfileiram no host.
  Mitigação hoje = host aplica coalescing/drop por capability (ex.: scan com
  `allow-duplicates=false`); backpressure real vem com streams p2.
- (−) Ordena-se por capability o significado de `event-kind` — um número mágico por evento.
  Documentado no WIT de cada capability e espelhado em enums no `CapabilityContract.cs`
  (ex.: `BleEventKind`).

## Decisões que precisam do Daniel (ou do spike)

1. **Backpressure / taxa de eventos** — aceitável começar com coalescing no host (sem
   backpressure), ou algum caso (áudio) já exige janela/limite explícito no v2? Recomendo
   começar simples (coalescing) e revisitar com sensores/áudio.
2. **Serialização dos eventos** — JSON por evento (simples) vs. binário flat para streams de
   alta frequência (BLE notify rápido, sensores). Recomendo JSON no v2; binário quando/ se
   um stream quente aparecer (troca de lowering, não de modelo).
3. **Teardown automático** — o guest .NET deve amarrar `unsubscribe` a `IDisposable`/
   `IAsyncDisposable` do handle de assinatura, pra não vazar em exceção. Detalhe de bindgen
   do guest, mas confirmar a ergonomia desejada.
