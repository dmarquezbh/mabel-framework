# Empacotamento do MabelHost como XCFramework + NuGet

Este documento descreve como `src/Mabel.Host.Ios` (pacote SwiftPM puro, alvo
`MabelHost`) é transformado num `MabelHost.xcframework` binário (device iOS +
iOS Simulator) e empacotado como conteúdo de um pacote NuGet, para distribuição
via um feed que o consumidor .NET (Opera) já autentica.

## Por que NuGet para um binário Swift

Decisão do Daniel (Tech Lead do Opera, 2026-08-17): em vez de o Opera consumir
`MabelHost` via `.package(path: "...")` apontando pro checkout local deste
repositório (que quebra em CI, onde o repo irmão não existe, e exige clone
manual em toda máquina de dev), o artefato passa a ser publicado no feed
privado Azure Artifacts `RuiBarbot`
(`https://pkgs.dev.azure.com/PjusInvest/_packaging/RuiBarbot/nuget/v3/index.json`)
que o Opera já usa e já autentica (Azure Artifacts Credential Provider local +
`NuGetAuthenticate@1` no CI) — reaproveitando essa infraestrutura em vez de
criar um mecanismo de distribuição novo. **O NuGet aqui é só o formato de
armazenamento/versionamento; o conteúdo não é um assembly .NET.**

⚠️ **Ressalva importante, já investigada antes desta sessão (PR #16772,
comentário em `Pjus.Opera.RuiBarbot.Native/lib/Package.swift`):** o SwiftPM
**não tem suporte nativo** ao protocolo do feed de Azure Artifacts — SwiftPM só
resolve `.package(url:)` via git ou registries que falam o protocolo SwiftPM
Registry (proposta SE-0292), e o feed `RuiBarbot` serve NuGet/npm/Maven/
Python/Cargo/Universal, não esse protocolo. Ou seja: publicar este `.nupkg` no
feed **não** torna `.package(path: "../../../../../../mabel-framework/...")`
substituível por um `.package(url: "https://pkgs.dev.azure.com/...")` direto no
`Package.swift` do lado consumidor. O pacote NuGet resolve o problema de
**armazenar e versionar o binário longe do checkout local do Daniel** — mas o
consumo real pelo `Package.swift` do host Skip (`lib/Package.swift`) ainda
precisa de um passo intermediário do lado .NET/build (ex.: um target MSBuild
que restaura o `.nupkg`, extrai o `.xcframework` do NuGet cache pra uma pasta
local, e só então o `Package.swift` referencia esse caminho local extraído em
vez do checkout irmão) — esse passo de integração fica fora do escopo deste
documento/script, que cobre só build+pack do artefato.

## Por que o `.framework` não sai pronto do `xcodebuild archive`

`src/Mabel.Host.Ios/Package.swift` declara um único produto de biblioteca sem
`type:` explícito (`.library(name: "MabelHost", targets: ["MabelHost"])` —
tipo "automatic"). Arquivar esse alvo via
`xcodebuild archive -scheme MabelHost SKIP_INSTALL=NO
BUILD_LIBRARY_FOR_DISTRIBUTION=YES` **não produz um `.framework`** — produz,
em `BuildProductsPath/<Config>-<sdk>/`:

- `MabelHost.o` — um objeto relocável único, resultado de um link `-r`
  (merged object) de todo o alvo;
- `MabelHost.swiftmodule/` — diretório com um slice por
  arquitetura-alvo (`.swiftmodule`, `.swiftinterface`, `.swiftdoc`, `.abi.json`);
- `MabelHost-Swift.h` (em `IntermediateBuildFilesPath/.../Objects-normal/arm64/`)
  — o header gerado de compatibilidade ObjC.

Isso é comportamento **conhecido e documentado** de arquivar pacotes SwiftPM
sem um target de Framework Xcode dedicado (a integração de SPM do Xcode
constrói para embutir no próprio esquema de build, não para exportar um
bundle `.framework` binário) — não é falha do projeto nem do build. O
`scripts/pack-xcframework.sh` monta o `.framework` manualmente a partir dessas
três peças (binário + swiftmodule + header) antes de rodar
`xcodebuild -create-xcframework`.

**Detalhe adicional encontrado nesta sessão:** a cópia do `.o` que fica em
`BuildProductsPath/<Config>-<sdk>/MabelHost.o` é, neste ambiente (Xcode 26.6),
um **symlink relativo quebrado** para
`InstallationBuildProductsLocation/Users/<usuário>/Objects/MabelHost.o`. O
artefato real e íntegro é a cópia que o próprio `xcodebuild archive` deposita
dentro do `.xcarchive`, em `Products/Users/<usuário>/Objects/MabelHost.o` — o
script localiza essa cópia via `find` (evita hardcodar o nome do usuário) em
vez de seguir o symlink de `BuildProductsPath`.

## O que o script faz

`scripts/pack-xcframework.sh [VERSION]` (default `VERSION=1.0.0`):

1. Limpa `build/` (estado anterior).
2. `xcodebuild archive` para `generic/platform=iOS` (device) e
   `generic/platform=iOS Simulator`, com
   `SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES
   CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO` — `MabelHost` é uma
   biblioteca sem app/entitlements, não precisa de assinatura de código
   (confirmado nesta sessão: os dois archives completaram com
   `CODE_SIGNING_ALLOWED=NO` sem nenhum erro relacionado a assinatura).
3. Monta `MabelHost.framework` para cada plataforma (binário + swiftmodule +
   header + `Modules/module.modulemap` + `Info.plist` sintético).
4. `xcodebuild -create-xcframework` combinando os dois `.framework`, saída em
   `build/xcframework/MabelHost.xcframework` — validado com 2 slices:
   `ios-arm64` (device) e `ios-arm64_x86_64-simulator` (simulador, fat
   arm64+x86_64 — cobre Mac Apple Silicon e Intel rodando o Simulator).
5. Zipa o `.xcframework` e empacota como conteúdo de um NuGet
   (`content/ios/MabelHost.xcframework.zip`) via `dotnet pack` sobre um
   `.csproj` vazio (`scripts/nuget/Pjus.RuiBarbot.MabelHost.Ios.csproj`) que só
   existe para processar o `.nuspec` já resolvido — não há `nuget.exe`/mono
   nuget disponível nesta máquina, e não há necessidade de instalar: o SDK
   `dotnet` (net10) já processa `<NuspecFile>` diretamente.

## PackageId e versão

- **PackageId:** `Pjus.RuiBarbot.MabelHost.Ios` — segue o prefixo `Pjus.` já
  usado pelos outros pacotes do feed `RuiBarbot` (`Pjus.RuiBarbot.Core`,
  `Pjus.Opera.RuiBarbot.Contracts`, `Pjus.SharedKernel`, ver
  `docs/ruibarbot-us4-migracao-submodule.md` e
  `docs/rui-native-handoff-igor.md` no repo Opera), com `.MabelHost.Ios` como
  sufixo identificando o artefato e a plataforma (o Android, quando existir
  o equivalente, seria `Pjus.RuiBarbot.MabelHost.Android` ou um AAR via Maven
  — ver ADR 0013, que já cobre D1/Android separadamente).
- **Versão:** `1.0.0` — primeira publicação. Não há tags de release neste
  repositório (`git tag -l` vazio no momento desta sessão) nem convenção de
  SemVer documentada; o ADR 0013 (docs/adr/0013-distribuicao-hosts-como-
  pacote-binario.md) já registra isso como pendência aberta ("Esquema de
  versão dos pacotes — atrelar à tag de release geral do Mabel, ou versionar
  cada pacote de forma independente?") e usa `version = "1.0"` fixo como
  convenção provisória do lado Android/AAR — `1.0.0` aqui segue a mesma lógica
  do lado iOS/NuGet.

## Reproduzindo

```bash
scripts/pack-xcframework.sh 1.0.0
```

Saídas (todas sob `build/`, git-ignorado — adicionar `build/` ao
`.gitignore` deste repo se ainda não estiver):

- `build/xcframework/MabelHost.xcframework`
- `build/nuget/Pjus.RuiBarbot.MabelHost.Ios.1.0.0.nupkg`

## Publicar (NÃO executado nesta sessão — decisão do Daniel)

```bash
dotnet nuget push build/nuget/Pjus.RuiBarbot.MabelHost.Ios.1.0.0.nupkg \
  --source PjusInvest-RuiBarbot \
  --api-key az
```

(`--api-key az` é o placeholder padrão quando a autenticação real é feita via
Azure Artifacts Credential Provider/MSAL, não uma chave literal — mesmo padrão
usado pelos outros pacotes do feed, ver `NuGet.Config` do Opera.)

## Limitações conhecidas desta primeira rodada

1. **Consumo real pelo SwiftPM ainda não resolvido** — ver ressalva no topo
   deste documento. Publicar o `.nupkg` não elimina, sozinho, o
   `.package(path: "../../../../../../mabel-framework/...")` em
   `Pjus.Opera.RuiBarbot.Native/lib/Package.swift`; falta o passo de
   integração do lado .NET/build que baixa o `.nupkg`, extrai o
   `.xcframework` pra uma pasta previsível, e só então aponta o
   `Package.swift` pra essa pasta local (em vez do checkout irmão).
2. **`.o` symlink quebrado em `BuildProductsPath`** — contornado lendo do
   `.xcarchive` em vez de seguir o symlink (ver seção acima). Não investigado
   se é específico desta versão de Xcode (26.6) ou comportamento geral.
3. **README ausente no pacote** — `dotnet pack` avisa (não bloqueia)
   recomendando um README no pacote; omitido de propósito nesta primeira
   versão por ser conteúdo puramente binário, sem necessidade de leitura via
   nuget.org/Visual Studio.
4. **Cold-cache do módulo SDK é lento** — a primeira execução do script após
   `rm -rf build/` recompila o cache de módulos Clang/Swift do zero (Auth,
   Foundation, UIKit, CoreBluetooth, CoreLocation etc. usados pelas
   capabilities) e pode levar vários minutos por plataforma; execuções
   subsequentes reusando o mesmo `derivedDataPath` seriam mais rápidas, mas o
   script sempre limpa `build/` no início (design deliberado — evita build
   incremental corrompido silenciosamente).
