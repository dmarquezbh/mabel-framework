# Spike — WasmKit rodando em runtime interpretado no iOS (2026-08-01)

- **Pedido:** Daniel (Tech Lead) — provar ou refutar, com dado real, o
  pré-requisito que `docs/hmr-e-estado.md` e `docs/ota.md` já apontam como
  bloqueador de tudo o mais: **dá pra carregar e EXECUTAR um `.wasm` via
  WasmKit no iPhone, e trocar esse módulo por outro em runtime, sem reiniciar
  o processo host?**
- **Esta sessão NÃO implementa HMR/OTA.** É um spike isolado, sem tocar no
  resto do Mabel (`src/Mabel.Host.Ios`, `MabelDevServer`, etc.).

## Resultado em uma frase

**WasmKit RODA e faz hot-swap real no runtime iOS** (confirmado no iOS
Simulator, arm64, com o toolchain/SDK real da Apple) — **mas a validação no
device físico test-device-1 ficou bloqueada por um limite de conta Apple Developer
não relacionado ao WasmKit** (ver §3). O achado técnico central — a memória
linear zera no swap, exatamente como `docs/hmr-e-estado.md` §1 previu — está
**confirmado empiricamente**, não é mais só teoria de design.

---

## 0. Achado prévio importante (antes de rodar qualquer coisa nova)

Antes de escrever código, investiguei se esse spike já tinha sido feito —
`docs/adr/0006-ota.md` e `docs/ota.md` citam "spike WASM-on-device (task #17,
concluído)" e "PROVADO no device sem Mac" para wasm2c-AOT e WasmKit. Achei:

- `docs/dotnet-aot-wasm/runner/` — um executável Swift que **roda no host
  macOS/Linux** (via `swift build`/`swift run`, sem device, sem xtool, sem
  assinatura) e usa WasmKit 0.2.2 pra instanciar um `.wasm` CORE emitido pelo
  `dotnet publish -r wasi-wasm`. Prova que **WasmKit consegue rodar wasm
  emitido pelo .NET** (com imports WASIp2 stubados) — mas isso não é "no
  device", é no Mac/Linux de dev.
- `docs/mabel-arquitetura-v2-ui-agnostic.md` (escrito nesta mesma sessão,
  **2026-08-01**, antes deste spike) já tinha feito essa mesma auditoria de
  forma independente e chegado à mesma conclusão: *"Nenhum host tem hoje um
  runtime WASM real instanciando um guest de verdade... `Package.swift` de
  `Mabel.Host.Ios`/`Mabel.Host.MacOS` não tem nenhuma dependência de
  WasmKit/wasmtime-swift"*. Ou seja, os selos "PROVADO no device" dos ADRs
  0006/0003 estavam **inflados** — o que existia era um runner de host, não
  uma prova em iOS de verdade. Vale o Daniel corrigir esse selo nos ADRs.

Este spike fecha essa lacuna real: é a primeira vez que WasmKit compila E
executa **visando a plataforma iOS** (SDK `iphoneos`/simulator da Apple, não
macOS/Linux) neste repositório.

---

## 1. O que foi construído

```
experiments/wasmkit-ios/
├── wasm/
│   ├── counter_v1.wat / counter_v1.wasm   (103 bytes)
│   └── counter_v2.wat / counter_v2.wasm   (103 bytes, difere em 2 bytes do v1)
└── app/
    ├── Package.swift        (produto .library, platform iOS 18+, dep WasmKit 0.3.1)
    ├── xtool.yml             (bundleID com.mabel.spike.wasmkit)
    └── Sources/WasmKitIOSSpike/
        ├── WasmKitIOSSpikeApp.swift   (SwiftUI shell — só exibe o log)
        ├── SpikeRunner.swift          (a lógica do spike, isolada de UI)
        └── Resources/{counter_v1,counter_v2}.wasm
```

### Os módulos de teste

`counter_v1.wasm`/`counter_v2.wasm`: WAT minúsculo (mão-escrito, compilado com
`wat2wasm` do wabt), 103 bytes cada, **mesma forma** (memória linear + 3
exports: `increment()→i32`, `get()→i32`, `version()→i32`), **byte-diferentes**
de propósito — v1 incrementa +1 e retorna `version()=1`; v2 incrementa **+10**
e retorna `version()=2`. Isso permite distinguir empiricamente "trocou de
verdade" de "reusou o v1 por cache/engano". Sem imports — não precisa de host
functions stubadas.

### O app

Swift Package no formato que `samples/hello-world-ios` já usa pra `xtool`
(produto `.library`, wrapper de app feito pelo `xtool`). `SpikeRunner.run()`:

1. Carrega os bytes dos dois `.wasm` do bundle.
2. Cria **um único `Engine()`** pro processo inteiro (a peça "cara"/reusável
   de um host real — Store/Module/Instance são o que se troca no hot-swap).
3. Instancia v1, chama `increment()` 5x, `get()`, `version()` — mede
   parse+instantiate com `ContinuousClock`.
4. **Hot-swap:** descarta a referência à store/module/instance do v1 (não há
   API de "unload" explícita — é ARC do Swift) e instancia v2 num **Store
   novo**, **mesmo Engine**, **mesmo processo** — repete 5x pra ter
   min/avg/max, não só a 1ª amostra (que pode carregar warmup do runtime).
5. **O teste central:** chama `get()` no v2 **antes** de qualquer
   `increment()` — se vier `0`, confirma que a memória linear nova nasceu
   zerada (não herdou o `5` do v1). Depois confirma `increment()==10` e
   `version()==2` pra provar que é o código NOVO rodando, não um cache do v1.
6. Reporta tudo via `print()` (stdout) e na tela (SwiftUI).

---

## 2. Resultado real — iOS Simulator, arm64, toolchain oficial da Apple (Xcode 26.6)

```
=== WasmKit-on-iOS spike ===
device: iPhone — iOS 26.5
processo: pid 48888

counter_v1.wasm: 103 bytes
counter_v2.wasm: 103 bytes

[v1] parse:        0.953 ms
[v1] instantiate:  0.703 ms
[v1] load total:   1.656 ms
[v1] increment() x5 → contador = 5
[v1] get()            → 5 (esperado 5)
[v1] version()        → 1 (esperado 1)

[v2] swap #1: parse 0.051 ms + instantiate 0.093 ms = 0.144 ms
[v2] swap #2: parse 0.034 ms + instantiate 0.075 ms = 0.109 ms
[v2] swap #3: parse 0.032 ms + instantiate 0.072 ms = 0.105 ms
[v2] swap #4: parse 0.031 ms + instantiate 0.072 ms = 0.103 ms
[v2] swap #5: parse 0.031 ms + instantiate 0.073 ms = 0.104 ms

[swap] min:  0.103 ms
[swap] max:  0.144 ms
[swap] avg:  0.113 ms

[v2] get() logo após o swap → 0
     ✅ ESTADO ZEROU no swap — confirma docs/hmr-e-estado.md §1
        ("a memória linear nova nasce zerada"). Preservar estado
        através do swap EXIGE um mecanismo explícito (serialize/
        restore, ADR 0003 opção b/c) — não é automático.

[v2] increment() 1x   → 10 (esperado 10, pois v2 soma +10)
[v2] version()        → 2 (esperado 2)
     ✅ confirma que o código NOVO (v2) está de fato rodando —
        não é cache/reuso do v1 (comportamento +10 é exclusivo do v2).

=== FIM DO SPIKE — sem crash, processo host seguiu vivo o tempo todo ===
```

### Leitura honesta dos números

- **Load inicial (v1): ~1,66 ms** (parse 0,95 ms + instantiate 0,70 ms).
  Inclui provavelmente algum custo de warmup do próprio `Engine`/runtime
  WasmKit na primeira instanciação do processo — não necessariamente
  representativo de um "load" de regime permanente.
- **Swap subsequente (v2): ~0,10-0,14 ms**, com a amostra caindo e
  estabilizando já na 2ª repetição (~0,10 ms) — **~15x mais rápido** que o
  load inicial. Pra um módulo de 103 bytes isso é evidência de que o
  **overhead do WasmKit em si (parse+instantiate) é desprezível** — o
  gargalo real de um HMR de verdade vai estar no tamanho/complexidade do
  módulo do app (um guest de verdade com centenas de KB a poucos MB), não no
  mecanismo de troca. **Não escala linearmente sem mais dado**: este número
  não deve ser lido como "troca de módulo custa 0,1 ms" em geral — é o piso
  medido com o menor módulo possível.
- **Estado ZEROU no swap** — bate exatamente com a previsão de
  `docs/hmr-e-estado.md` §1 ("não existe continuação gratuita"). Isso deixa
  de ser só design e passa a ser **fato medido**: qualquer HMR real no iOS
  **precisa** da camada (c)/(b) do ADR 0003 (estado externalizado + snapshot)
  — reload total sem isso é o comportamento padrão, confirmado.
- **Sem crash, sem trap inesperado** — o processo host sobreviveu ao
  descarte de uma instância WASM e à criação de outra, 5 vezes seguidas, sem
  degradar (não houve leak visível de memória entre as repetições, embora
  este spike não tenha instrumentado medição de memória — só tempo).

---

## 3. O que NÃO foi validado: device físico (test-device-1) — bloqueio de conta, não de código

Instalar no `test-device-1` (iPhone XS Max, iOS 18.7.9, UDID `[UDID-REDACTED]`)
via `xtool dev run` falhou, **sempre no mesmo passo** (registro do device na
Apple Developer API, antes de qualquer assinatura):

```
Error: Unexpected response, expected status code: created, response: forbidden(...
  detail: "Your development team has reached the maximum number of registered
  iPhone devices."
```

**Diagnóstico, com evidência (não é suposição):**

1. Confirmei que a conta já tem **3 iPhones cadastrados** via
   `xtool ds devices list` — `iphone 4` (2015, DISABLED), `iPhone de Daniel`
   (2019, DISABLED), `test-device-1` (2026, ENABLED). O erro **403** vem da API real
   da Apple (`DevicesCreateInstance`), não é um bug de parsing do xtool.
2. Verifiquei o código-fonte do xtool
   (`DeveloperServicesAddDeviceOperation.swift`): ele **já trata** o caso
   "device já existe" via **409 Conflict** (ignora e segue). O que recebemos
   é **403 Forbidden**, um erro diferente — a Apple está recusando a
   chamada de registro **mesmo pro device que já está na lista**, o que é
   consistente com contas **free/Personal Team** terem um teto **permanente**
   de dispositivos-iPhone registrados, sem o mecanismo de "reset anual" que
   só existe pra quem paga o Apple Developer Program (US$ 99/ano). Alternar
   `test-device-1` DISABLED→ENABLED (o mesmo truque que resolve outros problemas de
   slot, documentado em `docs/gerenciar-devices-apple-xtool-macos.md`) **não
   mudou o resultado**.
3. **Confirmei que o bloqueio é universal, não específico deste spike**:
   rodei `xtool dev run` também em `samples/capabilities-harness` (sample
   pré-existente, sem nenhuma relação com WasmKit) contra o mesmo device —
   **falhou com o EXACT MESMO erro 403**. Ou seja, **qualquer** deploy via
   xtool pra este device está bloqueado agora, independente do app.
4. Tentei o caminho alternativo (`xcrun devicectl device install app`
   direto, reaproveitando o `.app` que o `xtool dev build` já monta) — falha
   antes mesmo, por assinatura inválida (`xtool dev build` sozinho não
   assina pra device real; só `xtool dev run` completa o fluxo de
   certificado+profile, que é exatamente o que está bloqueado no passo 1).

**Não tentei contornar isso removendo/deletando os devices antigos
(`iphone 4`, `iPhone de Daniel`) na conta Apple Developer do Daniel** — seria
mexer numa conta real de terceiro sem decisão explícita dele nesta conversa, e
eu nem tenho acesso ao portal (developer.apple.com exige login interativo).
Isso fica como recomendação, não como ação tomada.

### O que resolve isso (decisão do Daniel, não engenharia)

- **Mais provável de funcionar:** logar em developer.apple.com → Certificates,
  Identifiers & Profiles → Devices, e tentar remover manualmente `iphone 4`
  (2015) e/ou `iPhone de Daniel` (2019) — devices mortos há anos. **Ressalva
  honesta:** contas free/Personal Team historicamente **não** costumam expor
  um botão de remoção sem o reset anual pago — pode ser que nem essa via
  funcione sem assinar o Apple Developer Program (US\$ 99/ano). Não tenho
  como confirmar sem o Daniel tentar (não tenho acesso ao portal).
- **Alternativa que resolve de vez:** assinar o Apple Developer Program
  (US\$ 99/ano) — sobe o teto de devices e destrava o reset anual. Decisão de
  custo, não minha.
- Uma vez destravado, rodar de novo é **~2 minutos**: `cd
  experiments/wasmkit-ios/app && xtool dev run` (usando o binário já
  compilado em `~/xtool-src-macos/.build/debug/xtool`, exportado como
  `$XTOOL`) — todo o código já está pronto e compila limpo pra
  `arm64-apple-ios` (confirmado, ver §4).

---

## 4. O que FOI confirmado sobre "iOS de verdade" (não é só simulador)

Mesmo sem instalar no device físico, **a compilação em si já é evidência
real**, não hipotética:

```
$ xtool dev build
...
Build complete! (13.82s)
Wrote to .../xtool/WasmKitIOSSpike.app
```

Isso compila e linka **WasmKit inteiro (132 arquivos) contra o SDK
`iphoneos` real da Apple** (arquitetura `arm64-apple-ios`, não
`arm64-apple-macosx` nem simulador) — usando exatamente o mesmo pipeline
(`xtool`) que os outros samples do Mabel já usam pra device físico. O
`Package.swift` do WasmKit 0.3.1 declara `platforms: [.macOS(.v15),
.iOS(.v18)]` oficialmente — **iOS é uma plataforma de primeira classe do
projeto**, não um hack. A única lacuna é a instalação (bloqueio de conta,
§3), não a compilação/execução (confirmada via simulador, mesma arquitetura
de execução do interpretador, §2).

---

## 5. Conclusão e próximo passo recomendado

**O spike CONFIRMA viabilidade técnica**, com uma ressalva de escopo
(device físico pendente por bloqueio de conta, não de engenharia):

1. ✅ WasmKit compila e linka de verdade pra iOS (SDK oficial, arquitetura
   `arm64-apple-ios`), usando a mesma dependência declarada oficialmente
   como suportando iOS 18+.
2. ✅ WasmKit **executa** um módulo `.wasm`, chama exports, lê/escreve estado
   em memória linear — confirmado rodando (iOS Simulator, mesma engine de
   interpretação que rodaria no device, já que WasmKit é puro-Swift sem
   JIT — não há diferença arquitetural relevante de execução entre simulador
   arm64 e device arm64 aqui, ao contrário de runtimes com JIT).
3. ✅ **Hot-swap funciona**: descartar uma instância e instanciar outra no
   mesmo processo, mesmo Engine, é rápido (~0,1 ms pro módulo mínimo testado)
   e não derruba o host.
4. ✅ **O estado zera no swap** — confirma empiricamente o que
   `docs/hmr-e-estado.md` §1 já previa por raciocínio. HMR/OTA real no iOS
   **exige** a camada de estado externalizado (ADR 0003 opção c/b); não tem
   almoço grátis.
5. ⛔ **Não confirmado ainda:** os mesmos números no device físico (só no
   simulador). Risco residual baixo — a lacuna é justamente a parte que
   **não** deveria variar entre simulador e device (WasmKit não tem JIT, não
   tem caminho de código diferente por plataforma) — mas "baixo risco" não é
   "zero risco", e o pedido original era device físico. Fica pendente.

**Próximo passo concreto, na ordem certa:**

1. **Daniel resolve o slot de device** (§3) — fora do meu alcance.
2. Rodar `xtool dev run` em `experiments/wasmkit-ios/app` contra o `test-device-1` —
   ~2 min, código já pronto, só falta esse desbloqueio.
3. **Só depois disso** vale integrar isso ao `InProcessGuestBridge.swift`
   real (`src/Mabel.Host.Ios/Sources/MabelHost/Capabilities/`) — trocar o
   fake in-process por uma instância WasmKit de verdade. Esse é o trabalho
   de "Fase 1" que `docs/mabel-arquitetura-v2-ui-agnostic.md` já identificou
   como o bloqueador comum de HMR, OTA e Flutter-embutido — mas essa doc
   recomendou começar pelo **Desktop** (wasmtime, JIT) por ser mais barato
   de provar HMR. Este spike mostra que o **iOS especificamente** (o
   interpretador, não o JIT) também está tecnicamente pronto pra esse
   próximo passo, assim que o device físico validar os números do simulador.
4. Não commitado/PR — só o código do spike em `experiments/wasmkit-ios/` e
   este relatório, como pedido.
