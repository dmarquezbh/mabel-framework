# Gerenciar devices da conta Apple Developer com xtool (conta free)

Como listar, desabilitar e reabilitar devices registrados na conta Apple Developer
usando um xtool recompilado — validado em WSL (Ubuntu 26.04 + Swift 6.1) com conta
**free** (sem Developer Program pago).

## Contexto

O provisioning free tem limite de devices por classe. Ao estourar, o deploy falha com:

```
403 FORBIDDEN_ERROR: Your development team has reached the maximum number
of registered iPhone devices.
```

O xtool de fábrica só **lista** devices (`xtool ds devices list`) — não expõe
disable/enable. Mas a API que ele usa (`developerservices2.apple.com/services/v1`,
mesma do App Store Connect) tem `PATCH /v1/devices/{id}` com `status: DISABLED|ENABLED`,
e **funciona em conta free**.

> A API **não tem delete** de device — só disable. Mas disable libera o slot do
> mesmo jeito.

## Recompilar o xtool com o subcomando `set-status`

### 1. Clonar na tag 1.17.0

O `main` do xtool exige Swift 6.2 (`swift-subprocess >= 0.5.0`); com Swift 6.1 use a tag:

```bash
git clone --depth 1 https://github.com/xtool-org/xtool ~/xtool-src
cd ~/xtool-src
git fetch --depth 1 origin refs/tags/1.17.0 && git checkout FETCH_HEAD
```

### 2. Patch: subcomando `ds devices set-status`

Em `Sources/XToolSupport/DSCommands/DSDevicesCommand.swift`, registrar
`DSDevicesSetStatusCommand.self` na lista de `subcommands` e acrescentar:

```swift
struct DSDevicesSetStatusCommand: AsyncParsableCommand {
    static let configuration = CommandConfiguration(
        commandName: "set-status",
        abstract: "Enable or disable a device"
    )

    @Argument(help: "The device id (from list)") var id: String
    @Argument(help: "ENABLED or DISABLED") var status: String

    func run() async throws {
        let client = DeveloperAPIClient(auth: try AuthToken.saved().authData())
        let newStatus: Components.Schemas.DeviceUpdateRequest.DataPayload.AttributesPayload.StatusPayload =
            status.uppercased() == "DISABLED" ? .disabled : .enabled
        let response = try await client.devicesUpdateInstance(.init(
            path: .init(id: id),
            body: .json(.init(data: .init(
                _type: .devices,
                id: id,
                attributes: .init(status: newStatus)
            )))
        ))
        switch response {
        case .ok(let ok):
            let device = try ok.body.json.data
            print("OK: \(device.id) name=\(device.attributes?.name ?? "?") status=\(device.attributes?.status?.rawValue ?? "?")")
        default:
            print("FAILED:")
            dump(response)
        }
    }
}
```

Reusa a sessão já autenticada de `~/.config/xtool/data/XTLAuthToken` — não pede
login novo.

### 3. Extrair `libxadi.so` do AppImage instalado

A lib anisette (`libxadi`, escrita em D) não vem no repo; o release embute no AppImage:

```bash
cd /tmp && mkdir xtool-extract && cd xtool-extract
/usr/local/bin/xtool --appimage-extract
mkdir -p ~/lib-xadi
cp /tmp/xtool-extract/squashfs-root/usr/lib/libxadi.so ~/lib-xadi/
```

> Copie a lib pra um diretório **limpo** (`~/lib-xadi`). Usar `-L` direto na pasta
> do AppImage faz o linker pegar a `libxml2.so.2` errada.

### 4. Build

Ubuntu 26.04 traz `libxml2.so.16`; o `libFoundationXML` do Swift 6.1 pede símbolos
versionados da `libxml2.so.2` antiga. Contorne com `--allow-shlib-undefined`:

```bash
swift build --product xtool \
  -Xlinker -L$HOME/lib-xadi \
  -Xlinker --allow-shlib-undefined
```

### 5. Rodar

Em runtime, aponte pro `libxadi` e pra `libxml2.so.2` do AppImage:

```bash
export LD_LIBRARY_PATH=$HOME/lib-xadi:/tmp/xtool-extract/squashfs-root/usr/lib:$LD_LIBRARY_PATH
XTOOL=~/xtool-src/.build/debug/xtool
```

## Uso

```bash
# listar devices (id, nome, udid, status)
$XTOOL ds devices list

# desabilitar (libera slot)
$XTOOL ds devices set-status <DEVICE_ID> DISABLED
# → OK: LPMRD9Q48Y name=iPhone de Daniel status=DISABLED

# reverter
$XTOOL ds devices set-status <DEVICE_ID> ENABLED
```

## Bônus: 403 mesmo com o device já registrado

O `xtool dev run` sempre re-POSTa o device (`devicesCreateInstance`) e confia no
`409 CONFLICT` pra detectar "já registrado". Só que a Apple valida o **limite antes
do dedupe** — time lotado devolve `403` mesmo pro device que já está na conta.

Patch em `Sources/XKit/DeveloperServices/Devices/DeveloperServicesAddDeviceOperation.swift`
(checar a lista antes de criar):

```swift
// skip registration if the device is already registered (Apple returns
// 403 max-devices before the 409 duplicate check on some accounts)
if let registered = try? await context.developerAPIClient.devicesGetCollection().ok.body.json.data,
   registered.contains(where: { $0.attributes?.udid?.lowercased() == targetDevice.udid.lowercased() }) {
    return
}
```

Validado em 2026-07-18: device desabilitado com sucesso em conta free e slot
liberado pro deploy.
