# Mabel Arquitetura v2 — UI-agnostic, fronteira com o WASM guest, HMR e OTA (2026-08-01)

- **Pedido:** Daniel (Tech Lead), consolidação de 4 pedidos acumulados na mesma
  sessão/direção estratégica: (1) Flutter como shell embutido coexistindo com a UI
  nativa (Igor também vai construir em Flutter — objetivo maior é o Mabel ser
  **UI-agnostic**); (2) toda regra de negócio mora sempre no módulo WASM/WASI guest
  (hoje escrito em C#), **independente** de qual adapter de UI está ativo; (3) HMR do
  guest em desenvolvimento; (4) OTA em produção.
- **Branch:** `spike/mabel-ui-agnostic-wasm-hmr-ota` (a partir de
  `spike/flutter-ui-agnostic`, que por sua vez parte de `feat/macos-native-build-flow`).
- **Este documento não substitui** `docs/mabel-flutter-ui-agnostic-avaliacao.md`
  (spike Flutter anterior, bloqueado por falta de SDK) — ele **retoma** esse spike
  agora que o SDK está instalado, **e** amarra os outros 3 pedidos aos ADRs que já
  existiam no repo antes desta sessão (0002, 0003, 0005, 0006, 0007), que eu não
  conhecia até ler o repo agora. A maior parte do trabalho de design já estava feita;
  o que faltava era conectar Flutter a esse desenho e ser honesto sobre o que está
  **desenhado** vs. **implementado de verdade**.

**Resultado em uma frase:** a arquitetura já projetada (SDUI + capabilities ABI +
HMR em camadas + OTA em 3 níveis) suporta os 4 pedidos como um roadmap coerente, mas
há uma lacuna estrutural que bloqueia os 4 ao mesmo tempo — **nenhum host tem hoje
um runtime WASM real instanciando um guest de verdade** (o macOS host tem um
`TODO: integrate WasmKit/wasmtime-swift`; o iOS host só tem uma capability bridge
**fake** in-process). Enquanto essa lacuna não fecha, Flutter (ou qualquer UI) só
consegue mostrar conteúdo estático, HMR não tem o que recarregar, e OTA não tem o
que distribuir.

---

## Achados que mudam a leitura do pedido do Daniel

Antes de desenhar qualquer coisa nova, três fatos do código/ADRs existentes
precisam estar na mesa, porque contradizem (ou qualificam bastante) o enunciado
literal do pedido:

1. **"o módulo WASM implementado em C#" tem um asterisco em iOS.** O ADR 0007
   (`docs/adr/0007-autoria-poliglota.md`) já registra, como fato de spike anterior:
   **`.NET→wasm não roda no WasmKit`** (o runtime WASM Swift puro do iOS). C#/Blazor
   é a linguagem "flagship" de **autoria/build-time/desktop** — o guest que roda
   **ao vivo no iOS** hoje é pensado como lean-core-wasm (Rust/TinyGo/AssemblyScript/C),
   não .NET interpretado. Existe uma segunda rota — `docs/dotnet-aot-wasm/` (NativeAOT-LLVM
   → wasm2c → C compilado nativo) — que o ADR 0006 cita como "provado" no device, mas
   **AOT é bakeado**: não dá pra trocar em runtime (ver item 3). Ou seja: se o guest
   do Daniel é C#, "HMR real no iOS" e "guest sempre C#" são, hoje, dois objetivos em
   tensão — não incompatíveis para sempre, mas não resolvidos pelo desenho atual.
2. **Nenhum host tem um runtime WASM real ligado ainda.** Grep em
   `src/Mabel.Host.MacOS` e `src/Mabel.Host.Ios` não encontra nenhuma dependência de
   WasmKit/wasmtime-swift em nenhum `Package.swift`. `MabelEngine.swift` (macOS) diz
   literalmente: `// TODO: integrate WasmKit/wasmtime-swift when the guest ABI lands` e
   hoje só renderiza `helloWorld()`/`glassDemo()` — arrays de `RenderCommand` hard-coded,
   não a saída de um guest de verdade. O que existe no iOS
   (`Capabilities/InProcessGuestBridge.swift`) é explicitamente um **fake in-process**
   ("NÃO há guest .wasm... o adapter real (WasmKit) substitui esta classe") — prova a
   forma da ABI (`GuestBridge`/`CapabilityWire.swift`, request-id + callback, exatamente
   como o ADR 0002 desenha), mas não executa WASM.
3. **O "HMR" que já funciona hoje não é o HMR do app nativo — é o preview web.**
   `MabelDevServer` (`src/Mabel.Core/Features/DevServer/MabelDevServer.cs`) observa
   `.razor`/`.cs`/`.css` do `web_app` Blazor, builda de verdade
   (`dotnet build -c Release`), e notifica clientes WebSocket com `reload:<versão>` —
   isso é **real e funcional**, mas serve `mabel.wasm` pra um **preview em navegador**
   ("vibe coding"), não pro app nativo iOS/macOS rodando no device. É a prova de
   conceito do *transporte* de HMR (arquivo mudou → rebuild → notifica → cliente
   rebusca), reaproveitável, mas não é ainda o hot-swap-no-host que o ADR 0003 desenha.

Nenhum desses achados invalida o pedido do Daniel — eles **reordenam** o roadmap: a
pré-condição real para os 4 itens não é "decidir o formato do IPC" (isso já está
decidido nos ADRs), é **terminar de ligar um runtime WASM de verdade em pelo menos
um host**. Ver seção de roadmap.

---

## 1. Fronteira UI-adapter vs. WASM-guest

### Regra de fronteira (a mesma para os 3 adapters, presente e futuro)

> **Todo adapter de UI é um renderer burro.** Ele recebe um de dois formatos de
> saída do guest — `RenderCommand`/`RenderOp` binário (canvas, `Mabel.Wasi.Protocol`)
> ou `SduiDocument` (controles nativos, `Mabel.Wasi.Protocol.Sdui`) — desenha ou
> monta a árvore de controles, captura input do usuário, e devolve isso ao guest
> como `InputEvent`/`SduiAction`. **Nenhuma regra de negócio, cálculo, validação ou
> decisão de fluxo pode viver no código do adapter** — nem Swift, nem Kotlin/Java,
> nem Dart/Flutter. Isso já é o padrão hoje (`MabelCanvasView`/`MabelView` só
> desenham e roteiam ação) — o pedido do Daniel é continuar essa regra pro adapter
> Flutter, não inventar uma nova.

O que cada adapter **pode** fazer sozinho, sem tocar o guest:
- Layout/animações puramente visuais (transições, easing, feedback tátil).
- Cache de frame anterior pra evitar flicker no re-render.
- Tratamento de erro de apresentação (ex.: cor de fallback se o payload vier malformado).

O que nenhum adapter **pode** fazer sozinho — sempre delega ao guest:
- Qualquer cálculo/regra (o exemplo do Daniel: regras de negócio).
- Navegação condicionada a estado de domínio (o guest decide o próximo descritor).
- Persistência de dado de domínio (o guest ou o host-store do ADR 0003, nunca o
  código do adapter).

### Mecanismo de IPC hoje — o que existe de verdade vs. o que está desenhado

| Canal | Formato | Estado real |
|---|---|---|
| Render (canvas) | `RenderCommand`/`RenderOp` binário, `Protocol.cs`↔`WasiContract.cs` (`mabel_init`/`mabel_update`/`mabel_render` exports; `draw_rect`/`draw_text`/... imports) | **Contrato bem especificado**, consumido por `MabelCanvasView.swift`. Fonte real dos comandos hoje: arrays estáticos (`MabelEngine.helloWorld()`), não um guest rodando. |
| Controles nativos (SDUI) | `SduiDocument`/`SduiNode`/`SduiAction` (`Mabel.Wasi.Protocol.Sdui.Descriptor`) | Contrato especificado (+ ADRs 0008-0012 de acessibilidade/responsividade/listas/navegação/versionamento), consumido por `MabelView.swift` via `MabelViewBuilder`. Mesma lacuna: falta o guest real emitindo o documento. |
| Capabilities (câmera, GPS, etc.) | `GuestBridge`/`CapabilityWire.swift` — `allocate/write/read` em memória linear + `invokeResult`/`invokeEvent` (request-id + callback, ADR 0002 D2) | **Forma da ABI provada** via `InProcessGuestBridge` (fake, testes/harness). Implementação real contra WasmKit **não existe neste repo ainda** (comentário no próprio código já antecipa o nome: "o adapter real (WasmKit) substitui esta classe"). |
| Dev loop / HMR transporte | HTTP + WebSocket (`MabelDevServer`) | **Real e funcional**, mas serve o preview web (browser), não o app nativo. |

Ou seja: a fronteira está **desenhada com precisão** (é provavelmente o ponto mais
maduro de todo o projeto — três ADRs de protocolo, mais os de acessibilidade/schema
versioning) mas **o fio que liga host↔guest de verdade num app rodando (iOS/macOS)
ainda não foi puxado**. Isso é o gargalo comum aos 4 pedidos do Daniel, não um
problema específico do Flutter.

### Onde o Flutter entra nessa fronteira

Dois modelos possíveis — só o primeiro é o que o Daniel pediu (view embutida
coexistindo com a nativa):

**Modelo A — Flutter como adapter embutido (add-to-app), host Swift continua dono
da ponte com o guest.** É a extensão direta do `IUiShell`/`FlutterShell` proposto no
spike anterior:

```
WASM guest (C#/lean-lang)
   ↕ WasiContract / CapabilityWire (Swift é quem fala com o guest)
Host Swift (dono do GuestBridge de verdade — WasmKit)
   ↕ Platform Channel (MethodChannel/EventChannel) ou FFI
FlutterHostView (FlutterViewController embutido via UIViewControllerRepresentable)
   → widget Flutter que interpreta RenderCommand/SduiDocument e desenha
```

Correção sobre o doc anterior: `MabelView`/`MabelCanvasView` são ambos
`UIViewRepresentable` (embrulham `UIView`). `FlutterViewController` é um
`UIViewController`, não uma `UIView` — o wrapper certo é
**`UIViewControllerRepresentable`**, não `UIViewRepresentable` (confirmado lendo
`MabelView.swift`). Detalhe pequeno, mas é exatamente o tipo de coisa que só aparece
lendo o código, não assumindo o precedente.

Nesse modelo, o Flutter **nunca fala com o WASM diretamente** — ele recebe do Swift
(via canal nativo) o mesmo `RenderCommand`/`SduiDocument` que os outros dois
adapters recebem, e devolve input do mesmo jeito. Isso preserva a regra de
fronteira sem esforço extra: o Dart do módulo Flutter vira só mais um "desenhista",
com o bônus de que o time do Igor pode iterar em Dart/widgets sem tocar Swift nem o
guest.

**Modelo B — Flutter como shell primário (dono da ponte com o guest), substituindo
o host Swift num alvo.** Existiriam pacotes Dart experimentais (`wasm_run` e
similares, via FFI para wasmtime/wasmer) que permitiriam ao próprio Flutter
instanciar o WASM sem um host nativo intermediário. **Não avaliado em profundidade
nesta sessão** — é um caminho maior, não pedido pelo Daniel agora (o pedido foi
"view Flutter embutida", não "Flutter substitui o shell nativo"), e duplicaria a
responsabilidade de dono-da-ABI que hoje é do Swift/Kotlin. Registro pra decisão
futura, não recomendo investir agora.

**Recomendação:** Modelo A. É o que estende `IUiShell` sem reabrir a arquitetura de
capabilities (ADR 0002) nem duplicar quem fala com o guest.

---

## 2. HMR — o que é real e o que seria só "restart rápido"

O ADR 0003 (`docs/adr/0003-hmr-e-estado.md`) já desenha isto com honestidade — a
tabela abaixo é esse ADR filtrado pela pergunta específica do Daniel (recarregar o
guest, ou a UI, sem reinstalar).

| Alvo do "HMR" | É HMR real? | Por quê |
|---|---|---|
| **Guest WASM no Desktop** (host .NET + wasmtime, JIT) | **Sim, será o mais barato de implementar** assim que o runtime real for ligado (hoje é placeholder) | JIT permite hot-swap de módulo; layer (c) do ADR 0003 (estado externalizado no host-store) sobrevive à troca; layer (d) (Roslyn Hot Reload) preserva estado sem nem trocar o módulo, pra edições não-"rude" |
| **Guest WASM no iOS, interpretado** | **Depende de uma validação que ainda não existe** | Precisa de um guest que rode *interpretado* no WasmKit — hoje isso só está provado pra lean-core-wasm, **não** pro guest C#/.NET do Daniel (achado #1 acima). Sem isso, o guest C# no iOS só tem o caminho AOT/wasm2c |
| **Guest WASM no iOS, AOT (wasm2c)** | **Não** — o próprio ADR 0003 é explícito: "iOS release with wasm2c AOT — **sem HMR**" | AOT compila pra código nativo bakeado no binário; trocar exige recompilar+reinstalar, não hot-swap |
| **UI adapter nativo (Swift)** | **Não** | Código Swift é compilado no binário do app; editar exige rebuild+redeploy. Não existe "hot reload de Swift" em produção |
| **UI adapter Flutter (Dart)** | **Parcialmente, só em dev, só via `flutter attach`** | O hot reload do Flutter é real, mas é um recurso do **Dart VM em modo debug com VM service aberto**, rodando via `flutter run`/`flutter attach` — não existe num build release embutido (add-to-app framework release, que é o único modo viável em device físico sem profile completo, conforme o spike anterior já apontava). Serve pro Igor iterar localmente rodando o `flutter_module` sozinho fora do app Mabel; não serve pro loop "editei Dart, o app Mabel no bolso do QA atualizou" |

**Conclusão honesta:** o único HMR que o pedido do Daniel vai sentir como "de
verdade" no curto prazo é o do **guest no Desktop** (uma vez que o runtime real
esteja ligado — que é o Passo 1 do roadmap). No iOS, o caminho realista *hoje* é:
desenvolver/iterar contra o host Desktop (loop rápido) e tratar o device físico como
validação periódica com reinstalação — não fingir HMR onde ele não existe. Isso
não é um "não" permanente: é uma pendência explícita do ADR 0003 ("Roslyn
metadata-update sob WasmKit — spike necessário") que continua em aberto.

---

## 3. OTA — a mesma honestidade, mapeada pro caso do Daniel

O ADR 0006 (`docs/adr/0006-ota.md`) já resolveu a pergunta de fundo — reaproveito o
modelo de 3 níveis dele, só conectando com "o que o Daniel quer atualizar":

1. **Nível 1 — descritor (SDUI) OTA.** Sempre seguro, em qualquer canal, inclusive
   loja pública — é dado, não código executável. Se o que muda com mais frequência
   é *layout/fluxo de tela* (não regra de negócio), isso já cobre uma fatia grande
   sem risco de compliance.
2. **Nível 2 — o módulo WASM guest (a "lógica") via interpretador.** Isto é
   provavelmente o que o Daniel quer dizer por "atualizar o app" (é a parte que muda
   mais rápido, como ele mesmo apontou). **Risco de compliance real e documentado**:
   a regra 2.5.2 da App Store Review Guidelines trata download de código executável
   como zona cinza — o JS tem um carve-out explícito, o WASM **não**. Isso não é
   uma opinião minha nem um bloqueio inventado: é uma leitura de policy que precisa
   de validação (jurídico/compliance da PJUS, não engenharia) antes de qualquer
   plano de produto assumir "OTA de lógica na loja pública". Em distribuição
   interna/enterprise/MDM (fora da loja pública), esse nível já é livre — se o app
   do Daniel for interno, esse caminho está desimpedido desde já (uma vez que o
   runtime real exista).
3. **Nível 3 — o shell nativo (Swift ou Flutter compilado).** Só pela loja. Embutir
   Flutter **não muda esse fato** — o binário Flutter/Dart embutido é código nativo
   compilado tanto quanto o Swift; atualizar *o shell* (nativo ou Flutter) sempre
   passa por review normal da loja.

**Pergunta em aberto que bloqueia decisão de produto (não é engenharia):** o app do
Daniel é distribuído pela App Store pública, ou é interno/enterprise/MDM (PJUS)? A
resposta muda o Nível 2 de "gray-area a validar com jurídico" para "livre desde já".
O ADR 0006 já registra isso como pendência; não tentei resolver aqui porque é
decisão do Daniel/compliance, não algo que dá pra inferir do código.

**Sequenciamento honesto:** OTA de Nível 2 reaproveita a mesma máquina de
"snapshot + swap" do HMR (ADR 0003) — trocar um módulo em produção é tecnicamente o
mesmo hot-swap que trocar em dev, só que disparado por um registry assinado em vez
de um `FileSystemWatcher`. Isso significa que **OTA de lógica não deveria ser
tentado antes do HMR de Desktop estar funcionando de verdade** — seria construir a
parte de distribuição antes de a parte de troca-em-runtime existir.

---

## 4. Spike funcional Flutter — o que rodou de verdade nesta sessão

Diferente da sessão anterior (bloqueada em `flutter --version`), desta vez o SDK
estava instalado e dava pra avançar. O que foi executado, de verdade, nesta máquina:

```
$ flutter --version
Flutter 3.44.8 • channel stable
```

```
$ flutter create -t module flutter_module
Wrote 12 files. All done!
```

```
$ cd flutter_module && flutter build ios-framework --output=../ios_frameworks --no-debug --no-profile
Building frameworks for com.example.flutterModule in release mode...
 ├─Copying Flutter.xcframework...
 ├─Building App.xcframework...           15.3s
 └─Moving to ../ios_frameworks/Release
Frameworks written to .../ios_frameworks.
```

Frameworks reais gerados e inspecionados:

| Framework | Tamanho | Conteúdo |
|---|---|---|
| `Flutter.xcframework` | **219 MB** | engine, slices `ios-arm64` (device) + `ios-arm64_x86_64-simulator` |
| `App.xcframework` | 6 MB | código Dart AOT compilado do módulo mínimo (hello world) |

O tamanho de `Flutter.xcframework` é bem maior do que a estimativa qualitativa
("dezenas de MB") do doc anterior — é um dado real agora, não uma suposição, e deve
entrar na conversa de custo/benefício com o Daniel: qualquer app Mabel que embuta
Flutter cresce ~200 MB+ no mínimo, antes de qualquer código do app.

**Passo adicional tentado (além do doc anterior): validar a alegação de que
"SwiftPM aceita `.binaryTarget` local pra `.xcframework` sem CocoaPods".** Criei um
pacote SwiftPM mínimo referenciando os `.xcframework` reais gerados acima e rodei:

```
$ swift package dump-package
```

O manifest **resolveu sem erro**, com os dois `binaryTarget`s (`Flutter`, `App`)
reconhecidos como `"type": "binary"` apontando pros paths reais. Isso confirma —
com dado real, não só leitura de documentação da Flutter — que a etapa de
`Package.swift` do plano anterior é viável. **Não fui além disso**: não tentei
`xcodebuild`/link para device real, não criei `FlutterHostView.swift`, não toquei
no `CreateProject.cs`/`XcodeNativeDeploy.cs` reais do Mabel, e não tentei instalar
no crow5. Motivo: cada um desses passos é trabalho de dias (estimativa da seção (d)
do doc anterior, que continua válida), não de um spike de continuidade — e o
bloqueio de provisioning profile (já documentado, independente do Flutter) segue
impedindo qualquer instalação real em device de qualquer jeito.

**Resumo do que mudou de "avaliado por leitura" pra "provado com dado real"
nesta sessão:**
- ✅ Flutter SDK funciona nesta máquina (3.44.8).
- ✅ `flutter create -t module` e `flutter build ios-framework --release` funcionam
  de ponta a ponta, sem CocoaPods, gerando `.xcframework` reais.
- ✅ SwiftPM aceita `.binaryTarget` local apontando pros `.xcframework` gerados
  (manifesto resolve).
- ❌ Ainda não provado: link real (`xcodebuild`) contra esses frameworks, execução
  de `FlutterViewController` dentro de um app Mabel de verdade, instalação em
  device físico (bloqueada por provisioning profile, bloqueio anterior e
  independente do Flutter).

Nenhum arquivo do spike Flutter (módulo, frameworks, pacote de teste) foi commitado
— tudo ficou em `/tmp/mabel-flutter-spike`, descartável, fora do repo.

---

## 5. Roadmap faseado

A ordem abaixo existe porque cada fase depende de dado real produzido pela
anterior — não é só "arquitetura primeiro por boa prática", é que HMR e OTA
literalmente não têm o que fazer sem a Fase 1.

```
Fase 0 (feito)      SDUI descriptor + capabilities ABI + protocolo de render
                     — DESENHADOS (ADRs 0001, 0002) e com a FORMA provada por
                     fakes/harness (InProcessGuestBridge). Não bloqueia nada
                     abaixo, mas não conta como "implementado".

Fase 1 (bloqueador   Ligar um runtime WASM real a pelo menos um host.
  de tudo o resto)   Recomendo começar pelo Desktop (.NET host + wasmtime, JIT,
                     ADR 0004) em vez do iOS: é onde HMR fica barato de provar
                     primeiro, e onde o guest C#/Blazor do Daniel roda sem o
                     asterisco do achado #1. Sem isso: Flutter só mostra
                     conteúdo estático (como o macOS hoje), HMR não tem o que
                     recarregar, OTA não tem o que distribuir.

Fase 2               Formalizar IUiShell/renderer no scaffold (mabel.json:
  (paralela à 1,      "renderer": "canvas"|"sdui"|"flutter") + FlutterHostView
  baixo custo)        via Modelo A (seção 1). Maioria é organizar o que já
                     existe (MabelCanvasView/MabelView já são 2 adapters de
                     fato). Puxa o SwiftPM binaryTarget + AssembleAppBundle
                     multi-framework signing + rpath (itens (d) do doc
                     anterior, ~6-11 dias). Pode andar em paralelo com a
                     Fase 1 porque é sobre o SHELL, não sobre o guest — mas só
                     vira "Flutter mostrando dado real" depois que a Fase 1
                     entregar algo pra desenhar.

Fase 3               HMR layer 1-3 do ADR 0003, primeiro no Desktop (uma vez
  (depende de 1)      que a Fase 1 tenha o wasmtime real ligado). Spike
                     separado decide se algo equivalente é possível no iOS
                     pro guest C# (achado #1) — sem esse spike, iOS trata o
                     device físico como validação periódica, não como loop
                     de HMR.

Fase 4               OTA Nível 1 (descritor) pode começar assim que houver
  (depende de 1 e 3)  um mecanismo simples de versionamento de descritor —
                     não depende de HMR. OTA Nível 2 (guest WASM) reaproveita
                     a máquina de snapshot+swap da Fase 3 — não faz sentido
                     antes dela. Decisão de produto pendente (App Store
                     pública vs. distribuição interna) determina se o Nível 2
                     é "livre" ou "gray-area a validar com jurídico".

Fase 5               Validação ponta a ponta no device físico (crow5) —
  (gated, não          gated pelo bloqueio de provisioning profile já
  estimável ainda)     documentado em docs/mabel-xcode-native-deploy.md,
                     anterior e independente de tudo isso.
```

**O que eu recomendaria priorizar primeiro, se o Daniel perguntar "por onde
começo":** Fase 1 no Desktop. É a única peça que, uma vez resolvida, desbloqueia
sinal real (não estático) pros outros três pedidos ao mesmo tempo — Flutter passa
a ter algo de verdade pra desenhar, HMR tem o que trocar, e a discussão de OTA deixa
de ser hipotética.

---

## Testes

Suíte existente rodada antes deste documento (nenhuma mudança de código nesta
sessão, só este `.md` foi adicionado — igual ao spike anterior):

```
Mabel.Wasi.Protocol.Tests.dll:   11 passed, 0 failed
Mabel.Renderer.Tests.dll:        26 passed, 0 failed
Mabel.Core.Tests.dll:            28 passed, 0 failed
Total: 65 passed, 0 failed
```

## Recomendação final

1. **Fronteira UI-agnostic:** o desenho já suporta Flutter como terceiro adapter
   sem reabrir a arquitetura — é extensão, não redesenho. Baixo risco.
2. **A pré-condição real dos 4 pedidos é a Fase 1** (runtime WASM real em pelo
   menos um host) — hoje ela não existe em nenhum host, e isso é mais urgente que
   qualquer decisão sobre Flutter especificamente.
3. **HMR real de curto prazo é uma história de Desktop, não de iOS** — ser
   honesto com o time sobre isso agora evita prometer um loop que o guest C# não
   suporta ainda no device físico.
4. **OTA de lógica (Nível 2) tem uma pergunta de produto pendente** (loja pública
   vs. distribuição interna) que muda o risco de compliance de "gray-area" pra
   "livre" — vale resolver essa pergunta antes de estimar esforço de engenharia
   pro registry/assinatura.

Nenhum PR foi aberto. A branch `spike/mabel-ui-agnostic-wasm-hmr-ota` contém só
este documento — decisão de merge/continuidade fica com o Daniel.
