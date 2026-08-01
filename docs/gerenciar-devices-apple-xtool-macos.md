# Gerenciar devices da conta Apple Developer com xtool (macOS)

Variante macOS de [gerenciar-devices-apple-xtool.md](gerenciar-devices-apple-xtool.md)
(validado lá em WSL Ubuntu 26.04 + Swift 6.1). Mesmo objetivo — reabilitar
`enable`/`disable` de device que o xtool de fábrica não expõe — mas o macOS
simplifica bastante o processo: **sem libxadi, sem AppImage, sem
LD_LIBRARY_PATH**.

## Por que é mais simples aqui

O xtool decide o provedor de anisette data (a identidade de dispositivo que a
API da Apple exige) por SO, em `Sources/XKit/GrandSlam/Anisette/ADIDataProvider.swift`:

```swift
static let liveValue: RawADIProvider = {
    #if os(Linux)
    return XADIProvider()       // emulação local via libxadi.so (D), extraída do AppImage
    #else
    return OmnisetteADIProvider() // servidor remoto (default: https://ani.sidestore.io)
    #endif
}()
```

No Linux não há framework nativo da Apple, daí o hack do `libxadi`. No macOS
(e Windows) o xtool já usa um **servidor Omnisette remoto** via HTTPS/WebSocket
— não usa AOSKit nem frameworks privados locais, e não precisa de nenhuma lib
nativa extra. Resultado prático: o binário buildado no macOS roda sozinho, sem
variável de ambiente nem wrapper.

## 1. Clonar

Testado com `main` (commit `697adae`), não a tag `1.17.0`: o Mac já tem Swift
6.3.3 nativo (`swift --version`), acima do mínimo que o `main` exige
(swift-subprocess >= 0.5.0, Swift 6.2+). Não há motivo para usar a tag antiga
aqui — ela existe só para travar quem está preso em Swift 6.1 (caso do WSL).

```bash
git clone --depth 1 https://github.com/xtool-org/xtool ~/xtool-src-macos
cd ~/xtool-src-macos
```

## 2. Patch: subcomando `ds devices set-status`

Mesmo patch do doc original, em
`Sources/XToolSupport/DSCommands/DSDevicesCommand.swift` — registrar
`DSDevicesSetStatusCommand.self` nos `subcommands` do `DSDevicesCommand` e
acrescentar o subcomando (ver o doc original para o código completo). Os tipos
gerados da OpenAPI (`Components.Schemas.DeviceUpdateRequest.DataPayload.*`) em
`Sources/DeveloperAPI/Generated/Types.swift` não mudaram entre a versão
validada no Linux e o `main` atual — o patch colou sem ajuste.

## 3. Anisette — nada a extrair

Diferente do Linux, **não há passo de extração de lib** aqui. O
`OmnisetteADIProvider` fala com `https://ani.sidestore.io` (servidor público do
projeto SideStore) via rede — funciona pronto, sem setup local.

## 4. Build

```bash
swift build --product xtool
```

Sem flags de linker, sem `--allow-shlib-undefined` (isso era workaround da
`libxml2` antiga do Ubuntu — não existe no macOS). Build limpo, ~2min40s numa
primeira compilação completa (~1750 módulos), binário Mach-O arm64 nativo em
`.build/debug/xtool`.

## 5. Rodar

Sem `LD_LIBRARY_PATH`, sem exports — o binário já roda direto:

```bash
XTOOL=~/xtool-src-macos/.build/debug/xtool

# autenticar (interativo — Apple ID/senha/2FA; ver seção abaixo)
$XTOOL auth login --mode password

# listar devices
$XTOOL ds devices list

# desabilitar (libera slot)
$XTOOL ds devices set-status <DEVICE_ID> DISABLED

# reverter
$XTOOL ds devices set-status <DEVICE_ID> ENABLED
```

## Autenticação (passo manual — não automatizável)

`xtool auth login` pede o modo de login — conta free **precisa** de
`--mode password` (o modo `key`/App Store Connect exige Developer Program
pago). O fluxo prompta interativamente: Apple ID → senha → seleção de time →
(se a Apple pedir) código 2FA. Isso usa `Console.getPassword`/prompts reais de
terminal — não dá pra automatizar via script/agente, precisa rodar num
terminal de verdade:

```bash
~/xtool-src-macos/.build/debug/xtool auth login --mode password
```

O token fica salvo (mecanismo de persistência do xtool, não documentado aqui
por não ser específico de macOS) e os comandos seguintes (`ds devices ...`)
reusam a sessão sem pedir login de novo.

## Integração com o mabel-framework

`mabel apple devices` (ver [mabel-apple-devices.md](mabel-apple-devices.md))
agora **detecta automaticamente** um build patcheado em
`~/xtool-src-macos/.build/{release,debug}/xtool` quando `MABEL_XTOOL` não está
setada e a máquina é macOS — não precisa exportar nada manualmente após seguir
os passos acima. `MABEL_XTOOL` continua funcionando como override explícito
(útil se o build estiver em outro caminho, ou pra apontar pro wrapper do
Linux). Ver `AppleDevices.PatchedXtoolCandidates()` em
`src/Mabel.Core/Features/Apple/AppleDevices.cs`.

## Bônus (não aplicado aqui)

O doc original também documenta um segundo patch, em
`DeveloperServicesAddDeviceOperation.swift` (403 mesmo com device já
registrado, no fluxo `xtool dev run`). Esse patch é para o **deploy**, não
para gestão de devices — fora do escopo desta receita; ver o doc original se
precisar dele.
