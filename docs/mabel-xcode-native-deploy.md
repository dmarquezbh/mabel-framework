# mabel deploy — Xcode nativo (sem xtool)

O `xtool` existe pra permitir dev iOS **sem Mac** (Linux/WSL). Num Mac de
verdade com Xcode.app instalado, isso e desnecessario: o mabel detecta o
ambiente e builda/instala/roda direto com `xcodebuild` + `xcrun devicectl`
(API do Xcode 15+). E aditivo — o `xtool` continua sendo o caminho certo
pra Linux/WSL, nada foi removido.

## Deteccao automatica

`mabel deploy` (plataforma iOS) escolhe o build tool assim:

1. `--build-tool xcode` ou `--build-tool xtool` — forca o fluxo.
2. Sem a flag: auto-detect via `XcodeEnvironment.IsNativeXcodeMac()` —
   verdadeiro quando `xcode-select -p` aponta pra um **Xcode.app completo**
   (termina em `Xcode.app/Contents/Developer`), nao so as Command Line
   Tools (`/Library/Developer/CommandLineTools`, que nao tem suporte a
   device iOS nem `devicectl`).

```bash
mabel deploy .                          # auto-detect
mabel deploy . --build-tool xcode       # forca Xcode nativo
mabel deploy . --build-tool xtool       # forca xtool (dev sem Mac)
mabel deploy . --build-tool xcode --device <UDID>   # device especifico
```

## O que o fluxo Xcode-nativo faz

Implementado em `Mabel.Core/Features/Deploy/XcodeNativeDeploy.cs`:

1. Compila o WASM do `web_app` e embute em `ios_app/Sources/<target>/Resources`
   (mesma logica do fluxo xtool — extraida pra `WasmResourceBundler.cs`, sem
   duplicacao).
2. Resolve o device: `--device <udid>` explicito, ou o primeiro device iOS
   retornado por `xcrun devicectl list devices` (ver abaixo).
3. Resolve o scheme via `xcodebuild -list -json` (le o `Package.swift` em
   `ios_app/`).
4. Builda: `xcodebuild build -scheme <scheme> -destination "id=<udid>"
   -derivedDataPath ios_app/.xcode-build -allowProvisioningUpdates
   CODE_SIGN_STYLE=Automatic`.
5. Instala: `xcrun devicectl device install app --device <udid> <path>.app`.
6. Roda: `xcrun devicectl device process launch --device <udid> <bundleID>`
   (bundle ID lido do `ios_app/xtool.yml`, que o scaffold ja gera — reaproveitado
   aqui em vez de duplicar o manifesto).

## Listagem de devices (`xcrun devicectl`)

`mabel devices` agora prefere `xcrun devicectl list devices` num Mac com
Xcode (mais moderno e confiavel que `idevice_id`/libimobiledevice, que
continua sendo o fallback — inclusive no Linux/WSL, onde `devicectl` nem
existe). Implementado em `Mabel.Core/Features/Devices/DevicectlDeviceLister.cs`.

Usa **sempre** `--json-output <arquivo>` — e a UNICA interface que o proprio
`devicectl --help` documenta como suportada pra consumo programatico. A
saida em tabela tem colunas com espaco interno (ex.: `connected (no DDI)`,
`iPhone XS Max (iPhone11,6)`) e nao da pra parsear com seguranca via split
de texto.

Importante: o JSON de `devicectl` tem **dois IDs diferentes** por device —
`identifier` (GUID interno do CoreDevice) e `hardwareProperties.udid`
(o UDID classico, o mesmo que `xcrun xctrace list devices` mostra). O mabel
usa **sempre o `udid`** em toda a stack (`-destination id=`, `devicectl
device install/launch --device`) porque e o formato aceito em todos os
comandos e evita confusao entre os dois.

## Signing automatico — requer Apple ID logado no Xcode

`CODE_SIGN_STYLE=Automatic` faz o Xcode gerenciar certificado + provisioning
profile sozinho, mas **precisa de uma conta Apple ID logada** em
Xcode > Settings > Accounts. Isso e configuracao manual via UI — nao da pra
automatizar sem interacao (nao existe API/CLI oficial da Apple pra logar
uma conta Apple ID num Xcode "do zero"). O mabel nao tenta contornar isso:
se o build falhar por falta de conta, o erro do `xcodebuild` aparece
integralmente (passthrough) e o mabel so acrescenta o contexto de causas
comuns.

## Verificacao real (2026-07-30)

Testado neste Mac (Xcode 26.6, iPhone XS Max fisico "test-device-1" iOS 18.7.9,
identifier USB `[UDID-REDACTED]`), rodando o pipeline completo
via `mabel create` → `mabel deploy --build-tool xcode`:

- ✅ Deteccao de Xcode nativo funcionou (`xcode-select -p` →
  `/Applications/Xcode.app/Contents/Developer`).
- ✅ `mabel devices` listou o device fisico corretamente via `devicectl`
  (nome, modelo, versao do iOS, UDID identico ao do `xctrace list devices`).
- ✅ Scaffold (`mabel create`) + compilacao do WASM (`web_app`) + resolucao
  de scheme (`xcodebuild -list -json`) funcionaram sem erro.
- ✅ `xcodebuild build` foi disparado com o destination correto
  (`id=[UDID-REDACTED]`, resolvido automaticamente).
- ⛔ **BLOQUEADO** no proprio `xcodebuild`, antes de qualquer signing: a
  "Platform" iOS do Xcode (componente separado desde o Xcode 15, requerido
  mesmo com o SDK de headers ja presente em `xcodebuild -showsdks`) nao
  esta instalada neste Mac. Erro exato:

  ```
  xcodebuild: error: Unable to find a destination matching the provided destination specifier:
      { id:[UDID-REDACTED] }
  Ineligible destinations for the "ios_app" scheme:
      { platform:iOS, arch:arm64e, id:[UDID-REDACTED], name:test-device-1,
        error:iOS 26.5 is not installed. Please download and install the
        platform from Xcode > Settings > Components. }
  ```

  `xcodebuild -downloadPlatform iOS` e `-downloadAllPlatforms` (CLI oficial
  do Xcode 15+ pra instalar isso sem abrir a UI) responderam
  `No matching downloadable found for platform: iOS` na tentativa rapida, e
  uma segunda tentativa com `-verbose` ficou pendurada (>15 min sem
  concluir nem falhar) — condizente com o catalogo/CDN de plataformas da
  Apple estar inacessivel ou nao-resolvivel neste ambiente, apesar de HTTPS
  generico pra `developer.apple.com` funcionar. Tambem confirmado: o
  `iOS DeviceSupport` bundled deste Xcode só vai ate a 16.4
  (`/Applications/Xcode.app/Contents/Developer/Platforms/iPhoneOS.platform/DeviceSupport/`),
  bem abaixo da 18.7.9 do device — e `xcrun devicectl list devices` mostra
  o device como `connected (no DDI)` (sem Developer Disk Image montada).

  **O que falta pra funcionar 100%:** instalar o componente "iOS" em
  Xcode > Settings > Components (UI interativa — unico caminho confirmado
  nesta maquina, ja que o download via CLI nao completou) e, separadamente,
  fazer login com um Apple ID em Xcode > Settings > Accounts pro
  `CODE_SIGN_STYLE=Automatic` funcionar. Nenhum dos dois foi forcado por
  workaround — sao passos manuais reais, documentados aqui como pedido.

- Confirmado tambem (com um Package.swift `.library`/`.target`, igual ao
  que `CreateProject.cs` ja gera, e um `.executableTarget`/`.executable` de
  teste): o bloqueio e **so** a Platform ausente, nao a forma do
  `Package.swift` — os dois casos falham com o mesmo erro na mesma etapa.
  Ou seja, o scaffold existente nao precisa mudar pra esse fluxo funcionar.

## Follow-up (2026-07-30) — Platform resolvida, novo bloqueio mais profundo

Retomado neste mesmo Mac apos o componente "iOS Platform" ser instalado via
Xcode > Settings > Components. Confirmado com `xcrun devicectl list devices`:
o iPhone fisico **test-device-1** (UDID `[UDID-REDACTED]`, iOS 18.7.9,
iPhone XS Max) aparece agora como `connected` (antes: `connected (no DDI)`).

Suite de testes unitarios existente (60 testes: `Mabel.Core.Tests` 23,
`Mabel.Wasi.Protocol.Tests` 11, `Mabel.Renderer.Tests` 26) — **todos
passando**, nenhuma regressao.

Rodando `mabel create hello-real --bundle-id com.mabel.hello-real` seguido
de `mabel deploy . --build-tool xcode --device [UDID-REDACTED]`:

- ✅ **O bloqueio original esta 100% resolvido**: `xcodebuild build -destination
  "id=[UDID-REDACTED]"` roda ate o fim e imprime `** BUILD SUCCEEDED **`
  contra o device fisico real — a mensagem "iOS 26.5 is not installed" nao
  aparece mais.
- ⛔ **Novo bloqueio, mais profundo, nunca alcancado antes** (o fluxo original
  falhava mais cedo, na resolucao de destino, entao isso nunca tinha sido
  exercitado de verdade): o build compila mas **nao produz nenhum `.app`**.
  Diagnostico, com evidencia:
  1. `xcodebuild -showBuildSettings -destination "id=<udid>"` retorna vazio
     e imprime `Supported platforms for the buildables in the current scheme
     is empty` — isso acontece **para qualquer product type** (testado com
     `.library`/`.target`, o scaffold atual, e com `.executable`/
     `.executableTarget`). Em ambos os casos o `xcodebuild build` "funciona"
     mas so gera um objeto relocavel (`.o`, caso library) ou um executavel
     Mach-O nu (caso executable) — nunca um bundle `.app`.
  2. Isso confirma que o empacotamento de um `Package.swift` isolado (sem
     `.xcodeproj`) num `.app` instalavel — Info.plist, wrapper de bundle,
     assinatura como Application — e feito **internamente pela IDE do Xcode**
     (Product > Run), e **nao existe via `xcodebuild` CLI** pra esse tipo de
     projeto. `swift package generate-xcodeproj` (que geraria um `.xcodeproj`
     de verdade com um target Application) **nao existe mais** no toolchain
     atual (`error: Unknown subcommand or plugin name 'generate-xcodeproj'`).
  3. Independente disso: `security find-identity -v -p codesigning` retornou
     **0 identidades validas** neste Mac agora — ou seja, nenhuma conta Apple
     ID esta de fato logada em Xcode > Settings > Accounts neste momento.
     Esse e exatamente o pre-requisito manual que este doc ja apontava como
     inevitavel (secao acima) — so que agora confirmado como realmente
     pendente, nao apenas hipotetico.

- **O que falta pra esse fluxo funcionar ponta a ponta contra hardware real**
  (2 itens independentes, ambos necessarios):
  1. **Manual, do Daniel:** logar com um Apple ID em Xcode > Settings >
     Accounts neste Mac (sem isso, nao ha identidade de assinatura pra
     nenhum caminho funcionar, nem o manual nem o automatico).
  2. **Engenharia, no `XcodeNativeDeploy`:** mesmo com conta logada, o
     `xcodebuild build` sozinho nao vai embrulhar o executavel num `.app`
     instalavel — falta implementar a montagem manual do bundle (criar
     `<Nome>.app/`, copiar o executavel + o `.bundle` de recursos ja gerado
     pelo SwiftPM pra dentro dele, escrever um `Info.plist` com bundle ID/
     versao/executavel, e assinar o bundle resultante com `codesign`) —
     essencialmente reimplementando, pra esse fluxo alternativo, uma fatia
     do que o `xtool` ja faz por conta propria. Isso e trabalho real de
     escopo proprio (nao uma correcao de 1-2 linhas), e so pode ser
     validado de ponta a ponta depois do item 1 acima. Nao foi implementado
     nesta sessao pra evitar codigo de assinatura/empacotamento nao
     testavel (sem identidade disponivel neste Mac agora, nenhuma tentativa
     de instalar no device teria como ser verificada de verdade).

Resumindo o estado real: o bloqueio de **plataforma** que motivou a retomada
deste teste esta confirmado e definitivamente resolvido. O fluxo **ainda nao
roda ponta a ponta** contra o device fisico — o motivo mudou, de "SDK da
plataforma ausente" pra "falta conta Apple ID + falta montagem manual do
`.app`", e o segundo item e trabalho de escopo proprio a ser planejado
separadamente.

## Follow-up (2026-07-31) — montagem manual do `.app` implementada, novo bloqueio (WWDR ausente)

Retomado apos o Daniel logar com Apple ID em Xcode > Settings > Accounts.

**Item 1 do follow-up anterior (montagem manual do `.app`) — implementado:**

1. **Scaffold corrigido** (`CreateProject.cs`): o produto do `Package.swift` do
   `ios_app` mudou de `.library`/`.target` pra `.executable`/`.executableTarget`.
   Confirmado com teste real: `xcodebuild build` agora produz um Mach-O
   executavel de verdade (`Ld .../Debug-iphoneos/ios_app normal`, sem a flag
   `-r` de objeto relocavel) em vez do `.o` que o `.library` gerava — sem essa
   troca, nao ha nada pra empacotar (um `.o` nao e um binario executavel).
2. **`XcodeNativeDeploy.AssembleAppBundle`** (novo): quando `xcodebuild build`
   sozinho nao produz um `.app` (caso do `Package.swift` puro, sem
   `.xcodeproj`), monta manualmente `<scheme>.app/` a partir do executavel —
   copia o binario, copia o resource bundle do SwiftPM (`<pacote>_<target>.bundle`)
   se existir, e escreve um `Info.plist` minimo (`BuildInfoPlist`, testavel
   isoladamente).
3. **`XcodeNativeDeploy.SignAppBundle`** (novo): assina o bundle com a primeira
   identidade "Apple Development" valida do keychain
   (`security find-identity -v -p codesigning`, parseada via
   `ParseFirstCodesigningIdentity`, testavel isoladamente).
4. Cobertura de teste: 5 testes novos em `XcodeNativeDeployTests.cs` (28 no
   total do arquivo) incluindo um cenario de integracao completo — pre-cria o
   executavel Mach-O falso, roda `Execute()` com `IShellExecutor`/`IFileSystem`
   fakes, confirma que o `.app` e montado com `Info.plist` correto e que
   `codesign` e chamado com a identidade certa.

**Verificado contra o test-device-1 real (BUILD SUCCEEDED, mesmo device/UDID dos testes
anteriores):** o fluxo agora avanca de "nenhum .app encontrado" pra tentar
assinar de verdade — prova que a montagem do bundle funciona ponta a ponta ate
o passo de assinatura.

**Novo bloqueio encontrado (nao e o item 1 do Daniel — e mais fundo):** mesmo
apos o Daniel criar o certificado "Apple Development" em Manage Certificates,
`security find-identity -v -p codesigning` continua retornando **0 identidades
validas**. Diagnostico: o certificado existe no keychain
(`security find-certificate -c "Apple Development: Seu Nome (TEAMID1234)"`
confirma, com validade `2026-07-31` a `2027-07-31`), mas o certificado
**intermediario** "Apple Worldwide Developer Relations Certification Authority"
**nao esta instalado** no keychain — sem ele a cadeia de confianca do
certificado de desenvolvimento nao valida, entao `find-identity` nao conta como
identidade utilizavel mesmo com cert+chave presentes. `SignAppBundle` ja
reporta essa causa raiz especifica no erro (nao so "sem identidade").

**O que falta pra rodar ponta a ponta contra o device fisico:**
1. **Manual, do Daniel:** instalar o certificado intermediario WWDR no
   keychain — normalmente Xcode faz isso sozinho ao adicionar a conta/gerar o
   certificado; como nao aconteceu aqui, o caminho mais direto e reabrir
   Xcode > Settings > Accounts, selecionar o time, e forcar um refresh (ou
   baixar o certificado G-series correspondente diretamente da pagina oficial
   da Apple Certificate Authority e importar no keychain).
2. **Ainda em aberto (fora do escopo verificavel sem o item 1 resolvido):**
   mesmo com identidade valida, instalar num device fisico normalmente exige
   tambem um **provisioning profile** embutido no bundle listando o UDID do
   device — `-allowProvisioningUpdates` no `xcodebuild` so gerencia isso pra
   schemes de projeto Xcode de verdade (com target Application), nao pro
   fluxo de `Package.swift` puro montado a mao aqui. Se o `codesign` passar
   mas o `devicectl device install app` falhar por profile ausente, esse e o
   proximo item de engenharia a resolver (fora do escopo desta rodada).
