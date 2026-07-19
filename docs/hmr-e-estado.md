# Mabel — HMR (Hot Module Reload) e preservação de estado

> **Fase 3.** Contrato/design. Irmão do ADR 0001 (SDUI) e 0002 (capabilities).
> Depende do spike WASM-on-device (WasmKit + xtool) e do renderer SDUI provarem
> o caminho antes de implementar. Nada aqui compila lógica: é design + ADR 0003.
> Decisão resumida em `docs/adr/0003-hmr-e-estado.md`.

## 1. O problema

O Mabel promete um loop de dev estilo Expo: **editar código → ver na tela em
segundos, sem redeploy**. Hoje o `mabel dev` (ver `MabelDevServer`) já faz metade:
observa arquivos, recompila o WASM, e avisa o cliente por WebSocket (`reload:<versão>`).
Falta a outra metade — **o que o host faz quando o novo WASM chega**:

1. **Como trocar o módulo** rodando por um novo sem reiniciar o app (*hot-swap*)?
2. **O que acontece com o estado** do app quando o módulo é trocado?

O item 2 é o difícil, e é o que o Daniel destacou. Um app tem estado: a tela em
que você está, o texto meio-digitado, a posição do scroll, os dados já carregados
da rede. Se cada edição joga tudo fora e volta pra tela inicial, o "hot reload"
vira "restart" — mata a ergonomia que justifica o recurso.

### Por que estado + WASM é intrinsecamente difícil

Um módulo WASM tem sua própria **memória linear**. Quando o host descarta o módulo
antigo e instancia o novo, **a memória linear nova nasce zerada**. Todo estado que
vivia *dentro* do guest (variáveis, heap do .NET/Go/Rust, a árvore de componentes
Blazor) **desaparece** no swap — a menos que seja explicitamente transportado.
Não existe "só continuar de onde parou" de graça: ou o estado vive fora do módulo,
ou é serializado antes e restaurado depois, ou se perde.

## 2. O loop de HMR (visão macro)

```
  arquivo .razor/.cs/.rs editado
        │
        ▼
  dev-server observa (debounce ~500ms)  ── já existe em MabelDevServer
        │
        ▼
  recompila guest → mabel.wasm (nova versão)
        │
        ▼  WebSocket "reload:<versão>"  ── já existe
  host recebe o sinal
        │
        ▼
  host busca o novo mabel.wasm (HTTP GET /mabel.wasm) ── já existe
        │
        ▼
  ┌─────────────────────────────────────────────┐
  │  HOT-SWAP (o que este doc desenha):           │
  │  1. preserva o estado (ver §4)                │
  │  2. descarta a instância antiga do módulo     │
  │  3. instancia o novo módulo                   │
  │  4. restaura/religa o estado                  │
  │  5. re-renderiza (guest emite novo descritor  │
  │     SDUI → host remonta os controles nativos) │
  └─────────────────────────────────────────────┘
```

O passo 5 casa de graça com o SDUI: re-render já é "guest descreve a árvore, host
reconcilia com os controles nativos". O HMR só precisa **disparar** um re-render
depois de garantir o estado.

### O runtime importa (dev vs release)

- **Desktop (loop primário de HMR):** runtime WASM com **JIT** (wasmtime/Cranelift).
  Instanciar um módulo novo é barato; hot-swap é trivial e rápido. Sem device, sem
  deploy — edita, salva, vê. É por isso que o desktop é o **loop de dev primário**
  (ver `docs/desktop.md`).
- **iOS dev:** **WasmKit (interpretador)**. Não há JIT (iOS proíbe), mas o
  interpretador **permite carregar/trocar módulos em runtime** — então hot-swap
  funciona no device, só mais devagar que o desktop. É o que habilita HMR no iPhone.
- **iOS release:** **wasm2c → C → arm64 nativo** (toolchain do xtool). AOT, sem
  hot-swap — release não tem dev-server. HMR é **exclusivamente um recurso de dev**.

## 3. Camadas de estado — o que existe pra preservar

Nem todo "estado" é igual. Separar ajuda a ser honesto sobre o que sobrevive:

| Camada | Exemplo | Dificuldade de preservar |
|---|---|---|
| **Estado de UI/navegação** | tela atual, aba, scroll, campo de form meio-preenchido | Fácil–média (é dado puro) |
| **Modelo de dados do app** | cards carregados, resultado de query, carrinho | Fácil–média (é dado puro) |
| **Recursos com handle nativo** | sessão de câmera aberta, stream de GPS, socket, timer | **Difícil** (handle vive no host/SO, não no guest) |
| **Operações async em voo** | chamada de capability pendente (reqId→continuation, ADR 0002) | **Difícil** (a continuation morre no swap) |
| **Animação em curso** | transição a meio caminho | Difícil (e raramente vale preservar) |

Regra honesta: **dado puro sobrevive; ligações vivas com o SO não**. Um socket
aberto, um `CLLocationManager` transmitindo, um `TaskCompletionSource` esperando o
callback de uma foto — tudo isso está atado à instância antiga. No swap, o correto
é **cancelar e religar**, não fingir que sobreviveu. Em particular, a tabela
`reqId → continuation` da ABI de capabilities (ADR 0002) deve ser **drenada
(timeout/cancel) no swap**; qualquer chamada nativa pendente é reemitida pelo novo
módulo se ainda fizer sentido.

## 4. As quatro opções de preservação de estado

### (a) Reload total — perde o estado

Swap o módulo, roda do entrypoint. A UI volta ao estado inicial.

- **Sobrevive:** nada.
- **Prós:** trivial, sempre correto, zero acoplamento. É o comportamento certo
  quando o *shape* do estado mudou de forma incompatível (ver §5).
- **Contras:** mata a ergonomia pro caso comum (ajustei uma cor, perdi a tela).
- **Papel:** **fallback** universal e baseline.

### (b) Snapshot — guest serializa antes, restaura depois

Antes do swap, o host chama um export do guest `mabel_serialize_state() → (ptr,len)`;
o guest serializa seu estado de app pra um buffer (JSON/MessagePack). O host guarda
os bytes, troca o módulo, e chama `mabel_restore_state(ptr,len)` no novo módulo, que
se reconstrói a partir do snapshot.

- **Sobrevive:** o que o dev decidiu serializar.
- **Prós:** guest mantém seu modelo de estado idiomático (heap normal); só precisa
  saber (de)serializar. Não impõe arquitetura à força.
- **Contras:** o dev escreve/mantém a (de)serialização; se o *shape* do estado mudou
  na edição, o `restore` pode falhar → precisa de desserialização tolerante/migradora
  (campos novos com default, campos removidos ignorados). Boilerplate.
- **Papel:** **mecanismo de transporte** — é *como* o estado atravessa um swap quando
  ele vive dentro do guest. Combina com (c) (o host é o custodiante dos bytes) e é a
  ponte pra guests não-.NET.

### (c) Estado externalizado num store do host — RECOMENDADO como padrão

O guest é escrito como **função de view pura**: `view(state) → descritor SDUI` e
`update(state, action) → state` (arquitetura estilo **Elm/Redux/TEA**). O **estado
vive num store do lado do host** (o host é o dono da lifetime), não na memória linear
do guest. No hot-swap:

1. o host **mantém** o store (não é tocado pelo swap);
2. instancia o novo módulo;
3. chama `view(state)` no novo módulo → novo descritor → re-render.

O estado sobrevive **por construção**, porque nunca esteve dentro do módulo trocado.

- **Sobrevive:** todo o modelo de app + UI/navegação (tudo que está no store).
- **Prós:** é a **única opção que compõe com hot-swap E com guests poliglotas** —
  o store é do host; C#/Go/Rust apenas descrevem view/update. Casa perfeitamente com
  o SDUI (o descritor **já é** uma função pura do estado). Também melhora testes e
  time-travel debugging de brinde.
- **Contras:** impõe um **modelo de programação** (reducers/estado imutável). Não é
  "escreva como quiser". E o host precisa custodiar o estado — na prática como um
  **blob opaco** que o guest (de)serializa (o host não entende os tipos de domínio),
  o que reintroduz o mecanismo de (b) para atravessar o swap. Ou seja: **(c) é a
  arquitetura; (b) é o transporte que a implementa**.
- **Papel:** **padrão arquitetural** do Mabel.

> **Nuance importante:** "store no host" não significa que o host entende o domínio.
> O caminho realista: o host guarda um **blob de estado opaco** cuja lifetime ele
> controla; a cada `update` o guest devolve o novo blob; no swap o host devolve o
> blob ao novo módulo (que faz `restore`). Assim a **lifetime (host)** desacopla da
> **semântica (guest)**. Se o edit não mudou o shape do estado, a preservação é
> trivial; se mudou, cai na migração best-effort de (b), ou no fallback (a).

### (d) Blazor/Roslyn Hot Reload — no caminho .NET, o padrão-ouro quando aplicável

No caminho de autoria .NET/Blazor, o Roslyn suporta **Hot Reload / metadata-update
(EnC)**: aplica *deltas* de IL a um processo **em execução**, **sem trocar o módulo**.
O estado literalmente não se move — fica tudo na memória do runtime mono-wasm.

- **Sobrevive:** **tudo** (não houve swap).
- **Prós:** o inner loop mais rápido possível e a melhor preservação de estado, sem
  arquitetura imposta ao dev.
- **Contras / limites reais:**
  - **NÃO se aplica no iOS.** O spike WASM-on-device provou que **.NET→wasm não roda no
    WasmKit** (o .NET emite WASI-preview2 Component + Mono; o WasmKit é core-module +
    preview1 → rejeita). Sem mono-wasm rodando no iOS, **não há onde aplicar o delta** →
    o iOS usa sempre swap + (c)/(b). O (d) vale onde um runtime que roda .NET-wasm
    existe: **desktop (wasmtime, JIT)** e provavelmente **Android (JIT)**.
  - **Só no runtime que roda .NET-wasm** (mono/interpretado). No caminho **release AOT**
    o Hot Reload **não existe** (AOT não aceita deltas de IL) — mas release não faz HMR
    de qualquer forma, então ok.
  - Só cobre **edições não-"rude"** (corpo de método). Edições "rude" (novo campo,
    mudar assinatura, mudar hierarquia de tipos) **não** aplicam por delta → precisa
    cair pro swap de módulo (c/a).
  - **Só vale pro guest .NET.** Guest lean (Rust/TinyGo/AssemblyScript/C) — que é o
    guest live-on-iOS — não tem Roslyn → usa (c)/(b)/(a).
- **Papel:** **otimização do caminho .NET no desktop/Android** — quando a edição é
  aplicável, evita o swap inteiro. No iOS e pros guests lean, degrada pro swap com
  estado externalizado.

## 5. Recomendação

Adotar uma **estratégia em camadas**, escolhida em runtime pelo host conforme o tipo
de edição e o runtime disponível:

1. **Padrão arquitetural: (c) estado externalizado num store do host.** É o alicerce
   — a única opção que sobrevive a hot-swap *e* funciona com guests poliglotas, e que
   já está alinhada ao SDUI (descritor = função pura do estado). Todo app Mabel deve
   ser estruturado como `view(state)` + `update(state, action)`.
2. **Transporte: (b) snapshot** (`serialize_state`/`restore_state`) é como o blob de
   estado atravessa um swap quando o guest é o dono da (de)serialização.
3. **Otimização .NET (desktop/Android): (d) Roslyn Hot Reload** para edições de corpo
   de método no guest .NET — evita o swap por completo, preserva 100%. Detecção de "rude
   edit" cai pro swap. **Não no iOS** (WasmKit não roda .NET-wasm — o iOS sempre usa
   swap+(c)/(b)).
4. **Fallback: (a) reload total** quando o shape do estado mudou incompatível, quando
   a desserialização falha, ou quando o dev pede reset explícito.

Ordem de decisão do host ao receber `reload:<versão>` (caminho .NET):

```
edição aplicável por metadata-update (Roslyn)?
  ├─ sim → aplica delta in-place (d). Estado 100% preservado. Fim.
  └─ não → hot-swap de módulo:
            serialize_state (b) do módulo antigo → blob no store do host (c)
            drena tabela reqId→continuation (cancela chamadas de capability em voo)
            descarta libera handles nativos vivos (câmera/GPS/socket/timer)
            instancia módulo novo
            restore_state (b) a partir do blob
            re-render via view(state)
            restore falhou (shape mudou)? → reload total (a)
```

### O que é honesto prometer

- **Sobrevive ao HMR:** tela/navegação, valores de formulário, scroll (se modelado no
  estado), dados carregados — tudo que estiver no store.
- **NÃO sobrevive (e é religado, não preservado):** sessões de câmera, streams de GPS,
  sockets, timers, chamadas de capability pendentes, animações em curso. O host os
  **encerra no swap** e o novo módulo os **re-subscreve** se ainda fizerem sentido.
  Fingir o contrário vazaria handles e continuations.

## 6. Wire / superfície de host (esboço, não implementação)

Exports do guest (irmãos dos de render/capability, lowering achatado core-module):

| Export | Assinatura | Papel |
|---|---|---|
| `mabel_serialize_state` | `() → (ptr,len)` | (b) snapshot pré-swap |
| `mabel_restore_state` | `(ptr,len) → status` | (b) restaura pós-swap |
| `cap_alloc` / `cap_free` | (já existem, ADR 0002) | ownership do buffer do blob |
| `mabel_render` | `() → descritor` (já no caminho SDUI) | re-render pós-restore |

Sinal de dev-server → host: `reload:<versão>` (já existe em `MabelDevServer`), mais
um canal opcional `hotreload-delta:<versão>` carregando o delta de metadata-update
do Roslyn quando o caminho (d) for aplicável (evita rebaixar pro swap).

O caminho (c) não precisa de novos exports além dos de (b): o "store" é o host
guardando o blob de `serialize_state` entre swaps.

## 7. Escopo / não-metas

- **É:** design do loop de HMR sobre o `MabelDevServer` existente; análise das 4
  opções de estado; recomendação em camadas (c padrão + b transporte + d otimização
  .NET + a fallback); mapa honesto do que sobrevive.
- **Não é (ainda):** implementação do hot-swap no host Swift/WasmKit nem no host
  desktop; integração real do Roslyn metadata-update no runtime .NET-wasm do desktop;
  diffing incremental de descritor SDUI (hoje re-render completo); migração automática
  de shape de estado (hoje é best-effort + fallback); HMR em release (não existe por
  design).

## 8. Pontos que precisam de decisão / validação

1. **Roslyn Hot Reload — resolvido pro iOS, aberto pro desktop.** O spike WASM-on-device
   já mostrou que **.NET-wasm não roda no WasmKit** → (d) **não** existe no iOS (o iOS
   usa sempre swap+(c)/(b); o guest live-on-iOS é lean-lang, sem Roslyn de qualquer
   forma). **Falta validar** o metadata-update rodando no runtime .NET-wasm do
   **desktop (wasmtime)** e do **Android (JIT)**, onde (d) é aplicável.
2. **Formato do blob de estado** — JSON (simples, igual SDUI v1) vs binário
   (MessagePack, menor/rápido). Recomendo JSON pra v1; trocar depois não muda o modelo.
3. **Modelo de programação obrigatório?** — Adotar (c) como padrão sugere um
   scaffold/framework de app estilo TEA. Confirmar com o Daniel se o Mabel *impõe*
   view/update ou se apenas *recomenda* (com (b) puro disponível pra quem não quiser).
