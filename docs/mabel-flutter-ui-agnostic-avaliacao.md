# Mabel UI-agnostic + spike Flutter embutido — avaliação (2026-08-01)

> **Continuação:** este spike foi retomado em `docs/mabel-arquitetura-v2-ui-agnostic.md`
> (SDK do Flutter já instalado, spike funcional avançado + HMR/OTA consolidados no
> mesmo roadmap). Este documento permanece como registro histórico do estado
> "SDK ausente" — leia o de v2 para o estado atual.

- **Pedido:** Daniel (Tech Lead), verbal — avaliar o Mabel ficar "UI agnostic" e permitir
  Flutter como shell alternativo; validar com um spike real chamando uma view Flutter
  a partir do app nativo (Rui Native), rodando no device físico **test-device-1**.
- **Branch:** `spike/flutter-ui-agnostic` (a partir de `feat/macos-native-build-flow`).
- **Resultado em uma frase:** o desenho arquitetural é viável e de baixo risco (o
  Mabel já tem o precedente certo — `MabelView` embrulha controles nativos num
  `UIViewRepresentable`, o mesmo mecanismo que embrulharia um `FlutterViewController`);
  **o spike funcional não rodou** porque o Flutter SDK não está instalado nesta
  máquina — parei no gate de pré-requisito, sem instalar toolchain grande sem
  aprovação prévia, como combinado.

---

## (a) Desenho de arquitetura proposto — Mabel UI-agnostic

### Estado atual (o que existe hoje, sem abstração formal)

O host iOS (`src/Mabel.Host.Ios/Sources/MabelHost/`) já tem **duas** formas de
apresentar UI, mas nenhuma abstração as une — a escolha é implícita, feita à mão
em `App.swift`/`ContentView.swift` de cada app gerado:

| Arquivo | O que faz | Mecanismo SwiftUI |
|---|---|---|
| `MabelCanvasView.swift` | Interpreta `RenderCommand`/`RenderOp` (espelha `Mabel.Wasi.Protocol`) e desenha via Core Graphics — o modo "canvas puro" mencionado nos comentários do `CreateProject.cs`. | `UIViewRepresentable` (provavelmente; ver nota abaixo) |
| `MabelView.swift` | Constrói uma árvore de **controles nativos UIKit** a partir de um `SduiDocument` (via `MabelViewBuilder`) e roteia ações de volta pro app. É o caminho ativo hoje — o comentário no próprio arquivo diz que substituiu o canvas ("preservado só como referência"). | `UIViewRepresentable` |

Ou seja: **o precedente que eu preciso pra propor Flutter já existe e já funciona**
— `MabelView` prova que "uma `View` Swift que embrulha um objeto UIKit arbitrário,
alimentado por um documento/estado vindo do lado .NET" é um padrão que o Mabel já
usa em produção. Um `FlutterViewController` é só mais um objeto UIKit (é literalmente
uma `UIViewController`) — o mesmo mecanismo de wrapper serve.

O que falta é **nomear e formalizar** essa escolha como uma porta (no sentido
Hexagonal que o resto do Mabel já usa em `IShellExecutor`/`IFileSystem`), em vez de
deixar implícita em qual `.swift` o scaffold decide gerar.

### Proposta: porta `IUiShell` (nome de trabalho) + adapters intercambiáveis

Seguindo o mesmo padrão de Ports-Adapters do `Mabel.Core`:

```
Mabel.Core.Ports
  IUiShell          <- porta (conceitual — decide COMO o app apresenta UI)

Adapters concretos (cada um um "shell"), hoje já 2 e propondo o 3º:
  1. NativeCanvasShell     -> MabelCanvasView.swift (RenderOp/CoreGraphics, "sem WebView")
  2. NativeControlsShell   -> MabelView.swift (SDUI -> UIKit nativo)
  3. FlutterShell          -> FlutterHostView.swift (NOVO — FlutterViewController embutido)
```

Diferença importante: `IUiShell` **não é uma interface C# chamada em runtime** (não
há um processo .NET rodando dentro do app iOS pra fazer dependency injection nesse
sentido — o "guest" é WASM/WASI, o host é Swift puro). É uma porta **arquitetural/de
scaffold**: uma escolha declarada em `mabel.json` (`"renderer": "canvas" | "sdui" |
"flutter"`) que o `CreateProject.cs` usa pra decidir **qual `.swift` gerar** em
`ContentView.swift`/`App.swift` — análogo a como `Platform` (`Domain/Platform.cs`)
já decide quais diretórios (`ios_app`, `android_app`, `desktop_app`) o scaffold cria.
A "porta" vive no nível de decisão de build, não de interface em tempo de execução.

Isso é consistente com o resto do Mabel: `IShellExecutor`/`IFileSystem` abstraem I/O
que o **C#** chama em runtime (dentro do processo `mabel` CLI); a escolha de shell
de UI abstrai o que o **scaffold** gera pro lado Swift, que roda fora do processo
.NET. São duas categorias de porta diferentes — não forçar `IUiShell` a ser uma
interface C# só por simetria estética seria over-engineering (seção "Escaláveis" dos
Pilares de Desenvolvimento pede evitar acoplamento desnecessário, não abstração por
abstração).

### Como o Flutter entraria como um shell concreto: add-to-app frameworks-only

O app iOS gerado pelo Mabel **não tem `.xcodeproj`, não tem CocoaPods** — só
`Package.swift` (SwiftPM puro). Isso descarta o caminho padrão de "Flutter
add-to-app" da documentação oficial (que assume Podfile). O caminho real, também
oficial e documentado pelo próprio time Flutter, é o modo **frameworks-only**:

```bash
# 1. Criar o módulo Flutter (fora do ios_app/, como um sibling)
flutter create -t module flutter_module

# 2. Buildar os frameworks .xcframework (sem CocoaPods, linkáveis direto)
cd flutter_module
flutter build ios-framework --output=../ios_app/Frameworks --no-debug --no-profile
# gera (no modo --release, o único viável pra device físico sem profile de Debug JIT):
#   Frameworks/Release/Flutter.xcframework   (motor Flutter/engine)
#   Frameworks/Release/App.xcframework       (o código Dart compilado AOT)
#   Frameworks/Release/<Plugins>.xcframework (se o módulo usar plugins nativos)
```

Isso é exatamente o que o pedido do Daniel já antecipou corretamente. O que isso
exige do pipeline existente:

| Etapa do pipeline hoje (`XcodeNativeDeploy.cs`) | O que muda com Flutter embutido |
|---|---|
| **`Package.swift`** (gerado por `CreateProject.ScaffoldIos`) | Precisa declarar os `.xcframework` como `.binaryTarget` e linkar no `.executableTarget` (`dependencies: ["Flutter", "App"]`). SwiftPM suporta `.binaryTarget(name:, path:)` pra `.xcframework` local — não precisa de repositório remoto nem checksum de URL pra um path local. |
| **`ContentView.swift`/`App.swift`** | Novo `FlutterHostView: UIViewControllerRepresentable` que instancia `FlutterViewController` (precisa de um `FlutterEngine` já rodando — engine tipicamente vive como singleton no `AppDelegate`/`@main` struct, não recriado a cada apresentação da view). |
| **`xcodebuild build`** | Precisa localizar e copiar os frameworks (SwiftPM `.binaryTarget` já resolve o link em build-time; não muda o comando). |
| **`AssembleAppBundle`** (monta `.app` manualmente, já que não há `.xcodeproj`) | **Muda de verdade.** Hoje só copia o executável Mach-O + o `.bundle` de recursos do SwiftPM pra dentro do `.app`. Com Flutter, precisa **também** copiar `Flutter.framework` e `App.framework` pra `<App>.app/Frameworks/` (frameworks dinâmicos embutidos, não linkados estaticamente) — é um passo novo, não uma extensão trivial do existente. |
| **`SignAppBundle`** (codesign) | Cada `.framework` dentro de `Frameworks/` precisa ser assinado **individualmente** antes do bundle principal (ordem importa: frameworks primeiro, depois o `.app` que os contém) — hoje o código só assina o bundle raiz uma vez. |
| **rpath do executável** | O executável precisa de um `LC_RPATH` incluindo `@executable_path/Frameworks` pro dyld encontrar os frameworks embutidos em runtime — isso normalmente é responsabilidade do linker do Xcode quando existe target Application de verdade; aqui, como o binário é montado a mão a partir de SwiftPM, **pode precisar de um passo explícito** (`install_name_tool -add_rpath` ou uma flag de linker no `Package.swift`) — não verificado nesta sessão (bloqueado antes de chegar lá, ver seção de spike). |
| **Provisioning profile** | Sem relação com o Flutter em si — é o bloqueio **já conhecido e documentado** em `docs/mabel-xcode-native-deploy.md` (instalação em device físico falha com `0xe8008015` por falta de profile gerenciado). Embutir Flutter não piora nem resolve esse bloqueio; ele bloquearia o install final de qualquer app gerado por este pipeline, com ou sem Flutter. |

### Viabilidade real

**Viável, mas não trivial** — não é "copiar 2 frameworks e pronto". Os pontos de
atrito genuínos, em ordem de risco:

1. **Assinatura multi-artefato** (frameworks + app, em ordem) é a peça que mais
   diverge do fluxo atual — `SignAppBundle` foi escrito assumindo um único bundle
   raiz sem conteúdo dinâmico dentro.
2. **rpath** é um detalhe fácil de esquecer e que só se manifesta como crash em
   runtime ("dyld: Library not loaded"), não como erro de build — precisa de teste
   real em device pra confirmar, não dá pra validar só lendo código.
3. **Tamanho do artefato**: `Flutter.xcframework` sozinho tem dezenas de MB por
   arquitetura — isso muda o perfil de tamanho do `.app` gerado (hoje um "Hello
   Mabel" nativo é minúsculo). Não é bloqueador, é custo a comunicar.
4. **Ciclo de vida do `FlutterEngine`**: precisar manter um engine "quente" (ideal
   pra warm-start da view Flutter) versus criar sob demanda é uma decisão de
   arquitetura própria — não é grátis em memória/tempo de start.
5. **`flutter build ios-framework` exige o modo `--release` (ou `--profile`) pra
   rodar em device físico sem um provisioning profile completo com capability de
   debug** — o modo `--debug` embute um runtime JIT que exige entitlements que só
   um profile "de verdade" com Apple Developer Program habilita. Ou seja: o mesmo
   bloqueio de provisioning profile do pipeline nativo **se agrava** com Flutter
   (o modo debug de Flutter é ainda mais dependente de profile completo que um
   app Swift puro).

Nada disso é um "não dá pra fazer" — é engenharia real de integração, não spike de
tarde. Ver estimativa de esforço na seção (d).

---

## (b) Spike funcional — o que foi tentado e onde parou

Pré-requisito explícito do pedido: verificar `flutter --version` antes de qualquer
tentativa de integração, e **parar e reportar** se o SDK não estiver instalado —
sem instalar toolchain grande sem aviso prévio.

```
$ flutter --version
zsh: command not found: flutter
```

Verificado também:
- `brew list --formula` / `brew list --cask` — Flutter não aparece em nenhuma das
  duas listas (não foi instalado via Homebrew).
- Nenhuma pasta `~/flutter`, `~/development/flutter` ou similar no `$HOME`.
- `pod` (CocoaPods) também não está instalado — que era esperado, já que o
  caminho avaliado (frameworks-only) não depende dele, mas confirma que não há
  nenhum resíduo de tooling Flutter/iOS nesta máquina.

**O spike parou exatamente aqui.** Não criei `flutter_module`, não rodei `flutter
build ios-framework`, não toquei em `Package.swift`/`ContentView.swift` do
`ios_app` gerado, e não tentei nenhum deploy contra o test-device-1 para a parte Flutter.
Instalar o Flutter SDK (download de ~1-2 GB + Xcode integration + aceite de
licenças) é uma ação de ambiente que não estava pré-aprovada — por isso parei e
estou reportando, em vez de instalar por conta própria.

O restante do pipeline nativo (build/bundle/codesign/install no test-device-1, sem
Flutter) **já estava validado em sessões anteriores** (ver
`docs/mabel-xcode-native-deploy.md`) e não foi refeito nesta sessão — não havia
motivo pra reexecutar o caminho nativo puro, já que o pedido era especificamente
sobre o caminho Flutter.

### Baseline confirmado nesta sessão

Antes de escrever este documento, rodei a suíte de testes existente pra garantir
que a branch parte de um estado limpo (nenhuma mudança de código foi feita —
só este `.md` foi adicionado):

```
Mabel.Core.Tests.dll:            28 passed, 0 failed
Mabel.Wasi.Protocol.Tests.dll:   11 passed, 0 failed
Mabel.Renderer.Tests.dll:        26 passed, 0 failed
Total: 65 passed, 0 failed
```

Ambiente confirmado nesta máquina (pra reprodução futura do spike):
- Xcode 26.6 (`/usr/bin/xcodebuild`), `.NET` SDK `10.0.302`.
- Device físico **test-device-1** (iPhone XS Max, iOS 18.7.9) conectado via USB e
  emparelhado: `xcrun devicectl list devices` reporta `available (paired)`.
- Flutter SDK: **ausente** (bloqueador do spike, não do restante do pipeline).

---

## (c) Bloqueios reais

1. **Bloqueador do spike (novo, encontrado nesta sessão): Flutter SDK não
   instalado.** Impede qualquer passo prático de integração (`flutter create`,
   `flutter build ios-framework`) — não dá pra avançar sem instalar o SDK, decisão
   que cabe ao Daniel (tamanho do download, aceite de licença Flutter/Google,
   impacto em disco).
2. **Bloqueador já conhecido e documentado (não é novo, não tentei resolver de
   novo): provisioning profile.** Mesmo com o Flutter SDK instalado e a
   integração de frameworks funcionando, o **install final no test-device-1** passaria
   pelo mesmo bloqueio já registrado em `docs/mabel-xcode-native-deploy.md`
   ("Follow-up 2026-07-31... provisioning profile" — `com.apple.dt.CoreDeviceError
   error 3002 / 0xe8008015`). Esse bloqueio é **anterior e independente** do
   Flutter: afeta qualquer app gerado por este pipeline hoje, nativo ou com
   Flutter embutido. Como pedido explicitamente, não tentei contorná-lo de novo
   aqui.
3. **Risco técnico adicional específico do Flutter** (não testado, ver seção a):
   assinatura multi-framework em ordem, rpath do executável, e o fato de que o
   modo `--debug` do Flutter é ainda mais dependente de um provisioning profile
   completo que o app nativo puro — ou seja, mesmo resolvendo o bloqueio 2
   genérico, a variante Flutter pode precisar de configuração adicional de
   capabilities no profile.

**Honestamente: o "Hello from Flutter" chamável a partir do `ContentView.swift`
não foi produzido nesta sessão.** O que existe é a avaliação arquitetural (viável,
caminho técnico claro) e a confirmação de que o ambiente desta máquina não tem o
pré-requisito mínimo pra sequer começar a tentativa prática.

---

## (d) Esforço estimado para uma versão robusta (não apenas spike)

Assumindo que o Flutter SDK seja instalado e que o bloqueio de provisioning
profile do pipeline nativo seja resolvido separadamente (é pré-requisito
comum aos dois caminhos, nativo e Flutter — não é trabalho extra do Flutter):

| Item | Esforço estimado | Observação |
|---|---|---|
| `CreateProject.cs`: gerar `flutter_module` + wiring de `mabel.json` (`"renderer": "flutter"`) | 0.5-1 dia | Baixo risco — mesmo padrão dos outros `Scaffold*` já existentes. |
| `Package.swift`: `.binaryTarget` pros `.xcframework` + `FlutterHostView.swift` (`UIViewControllerRepresentable`) | 1-2 dias | Precisa de iteração real em device pra achar os detalhes de rpath/lifecycle do engine — não dá pra validar só por leitura de código. |
| `XcodeNativeDeploy.AssembleAppBundle`: copiar frameworks pra `Frameworks/` + `SignAppBundle`: assinar em ordem (frameworks → app) | 2-3 dias | O item de maior risco técnico real — é onde apps "quase funcionando" costumam falhar em runtime (dyld) de forma difícil de diagnosticar sem device físico. |
| Pipeline de build do módulo Flutter integrado ao `mabel build`/`mabel deploy` (chamar `flutter build ios-framework` como etapa, cachear/invalidar) | 1-2 dias | Depende de decisão de produto: rebuildar Flutter a cada deploy é lento (build Dart AOT); provavelmente quer cache com invalidação por hash do módulo. |
| Testes (`Fakes` de `IShellExecutor`/`IFileSystem` cobrindo os novos comandos `flutter build`, a nova lógica de `AssembleAppBundle` com frameworks, a nova ordem de assinatura) | 1-2 dias | Seguindo o padrão já usado em `XcodeNativeDeployTests.cs` (fakes, não mocks). |
| Validação ponta a ponta real no test-device-1 (depende do bloqueio de provisioning profile estar resolvido) | 0.5-1 dia, **mas gated** | Não dá pra estimar com confiança até o bloqueio de profile ser resolvido — pode revelar mais problemas de assinatura/rpath só visíveis em device real. |
| **Total** | **~6-11 dias úteis de engenharia focada**, **fora** do tempo já gasto (não recorrente) resolvendo o bloqueio de provisioning profile do pipeline base | Estimativa de uma pessoa sênior familiarizada com o codebase; primeira iteração de um dev novo no projeto tende a ser mais lenta pelos detalhes de assinatura/dyld, que são conhecidos por serem difíceis de depurar às cegas. |

Isso é a versão "funciona de verdade, com testes, no pipeline oficial do
`mabel deploy`" — não uma segunda tentativa de spike manual. Um spike manual
(sem integrar no `CreateProject.cs`/`XcodeNativeDeploy.cs`, só provando o
`FlutterHostView` num projeto Xcode ad-hoc com `.xcodeproj` de verdade) seria
mais rápido — talvez 1-2 dias — mas não usaria o pipeline hand-rolled do Mabel
e não provaria a parte mais arriscada (assinatura multi-framework sem
`.xcodeproj`).

---

## Recomendação

1. **Arquitetura:** formalizar a escolha de shell de UI (`"renderer":
   "canvas"|"sdui"|"flutter"` em `mabel.json`) como próximo passo de baixo custo
   — é majoritariamente organizar o que já existe (`MabelCanvasView`/`MabelView`
   já são dois adapters de fato) e dar um nome ao terceiro.
2. **Flutter prático:** decidir se vale instalar o SDK antes de investir os
   ~6-11 dias de (d) — o caminho técnico existe e é sólido, mas carrega peso real
   (tamanho de binário, assinatura multi-framework, dependência dupla do
   bloqueio de provisioning profile). Não é um "sim" ou "não" barato de reverter
   depois — recomendo tratar como uma decisão de produto explícita do Daniel,
   não uma continuação automática deste spike.
3. **Provisioning profile** continua sendo o bloqueio mais urgente e mais barato
   de resolver primeiro (afeta 100% dos deploys físicos hoje, com ou sem
   Flutter) — já está escopado à parte em `docs/mabel-xcode-native-deploy.md`.

Nenhum PR foi aberto. Esta branch (`spike/flutter-ui-agnostic`) contém só este
documento — decisão de merge/continuidade fica com o Daniel.
