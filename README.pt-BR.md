# Mabel Framework

> [Read in English](README.md)

**Mabel** e um framework cross-platform mobile/desktop que usa **.NET/Blazor** como linguagem de UI, compilado para **WASM/WASI**, e renderizado via **canvas nativo** — sem WebView.

## Por que Mabel?

Os frameworks cross-platform existentes te forcam a usar WebViews (Ionic, Capacitor), linguagens proprietarias (Flutter/Dart), ou JavaScript (React Native). O Mabel tem uma abordagem diferente:

- **Escreva UI em Blazor** (arquivos `.razor`) — C#, nao JavaScript
- **Compile para WASM/WASI** — portavel, sandboxed, sem runtime de navegador
- **Renderize em canvas nativo** — Core Graphics (iOS), SkiaSharp (Desktop), Canvas (Android)
- **Sem WebView** — performance real de renderizacao nativa
- **Hot reload** — servidor dev estilo Expo que envia mudancas para o celular em tempo real

## Arquitetura

```
  Componentes Blazor (.razor)
        |
        v
  Compilados para WASM/WASI
        |
        v
  Host Nativo carrega modulo WASM
        |
        v
  Comandos de Renderizacao (Protocolo WASI)
        |
        v
  Canvas Nativo (Core Graphics / SkiaSharp)
```

1. **Componentes Blazor** (arquivos `.razor`) sao compilados para WASM/WASI — sem navegador, sem WebView
2. Um **host nativo** (Swift no iOS, Kotlin no Android, .NET no Desktop) carrega o modulo WASM
3. Renderizacao via **canvas nativo** (Core Graphics no iOS, SkiaSharp no Desktop)
4. Um **protocolo WASI** conecta guest (WASM) e host (nativo) com comandos de renderizacao
5. O **CLI `mabel`** e construido com .NET 10 AOT

## Estrutura do Projeto

```
mabel-framework/
  Mabel.sln                          # Solution (6 projetos)
  setup.sh                           # Instalador de dependencias
  src/
    Mabel.Cli/                       # Entrypoint do CLI (fino, AOT)
      Program.cs                     # Parsing de args, delega para features
    Mabel.Core/                      # Features, ports, infraestrutura
      Domain/
        Platform.cs                  # Enum Platform (Ios, Android, Desktop, All)
        ToolRequirement.cs           # Registro de KnownTools
      Ports/
        IShellExecutor.cs            # Abstracao de shell
        IFileSystem.cs               # Abstracao de file system
      Infrastructure/
        BashShellExecutor.cs         # Implementacao real do shell
        LocalFileSystem.cs           # Implementacao real do file system
      Features/
        Doctor/DiagnoseEnvironment.cs
        Setup/RunSetup.cs
        Scaffold/CreateProject.cs
        Deploy/DeployToDevice.cs
        Devices/ListDevices.cs
        DevServer/MabelDevServer.cs  # Servidor HTTP + WebSocket hot reload
        DevServer/RunDevServer.cs
        UsbHelp/UsbGuide.cs
    Mabel.Wasi.Protocol/             # Protocolo de renderizacao WASI
      Protocol.cs                    # RenderOp, RenderCommand, InputEvent
      WasiContract.cs                # Nomes de funcoes Guest/Host
    Mabel.Renderer/                  # Renderizador agnositco de plataforma
      ICanvas.cs                     # Abstracao de canvas
      MabelRenderer.cs               # Interpreta RenderCommands -> ICanvas
    Mabel.Host.Ios/                  # Host nativo iOS (Swift Package)
      Sources/MabelHost/
        MabelCanvasView.swift        # Renderizador Core Graphics
        MabelView.swift              # Wrapper SwiftUI
        MabelEngine.swift            # Integracao com runtime WASM
  tests/
    Mabel.Core.Tests/                # 5 testes (DiagnoseEnvironment)
    Mabel.Renderer.Tests/            # 16 testes (MabelRenderer)
  samples/                           # Projetos de exemplo (em breve)
```

### Arquitetura: Vertical Slice + Hexagonal

- **Vertical Slice**: Cada feature e auto-contida na sua propria pasta em `Features/`
- **Hexagonal/Ports-Adapters**: Todas as dependencias externas atras de interfaces (`IShellExecutor`, `IFileSystem`). Adapters reais em `Infrastructure/`, fakes nos projetos de teste
- Apenas 2 projetos .NET para codigo da app: `Mabel.Core` (features + ports + infra) e `Mabel.Cli` (entrypoint fino)

## Comandos do CLI

```bash
mabel doctor            # Verificar ambiente (ferramentas, PATH, deteccao WSL)
mabel setup             # Instalar dependencias (.NET 10, Swift, xtool, wasmtime)
mabel setup --uninstall # Remover dependencias instaladas
mabel create <nome>     # Criar scaffold de um novo projeto Mabel
mabel deploy [caminho]  # Compilar e executar em dispositivo/emulador
mabel dev [caminho]     # Iniciar servidor dev com hot reload (estilo Expo)
mabel devices           # Listar dispositivos conectados
mabel usb-help          # Guia de configuracao USB para dispositivos fisicos
mabel version           # Mostrar versao
```

Opcoes:
- `--platform, -p` — Plataforma alvo: `ios`, `android`, `desktop`, `all`
- `--bundle-id, -b` — Bundle ID para create (padrao: `com.example.<nome>`)
- `--port, -P` — Porta do servidor dev (padrao: 5555)
- `--verbose` — Saida verbosa para o servidor dev

## Protocolo de Renderizacao WASI

O guest (WASM) envia comandos de renderizacao para o host (nativo) como structs planas:

| Op          | Campos Usados                                              |
|-------------|-----------------------------------------------------------|
| BeginFrame  | Color (fundo)                                              |
| Rect        | X, Y, W, H, Color                                         |
| RoundRect   | X, Y, W, H, Radius, Color                                 |
| Circle      | X (cx), Y (cy), Radius, Color                             |
| Line        | X (x1), Y (y1), W (x2), H (y2), Color                    |
| Text        | X, Y, Text, FontSize, Color                               |
| Image       | X, Y, W, H, Text (ID da imagem)                           |
| PushClip    | X, Y, W, H                                                |
| PopClip     | (nenhum)                                                   |
| PushOpacity | X (alpha 0-1)                                              |
| PopOpacity  | (nenhum)                                                   |
| Translate   | X (dx), Y (dy)                                             |
| EndFrame    | (nenhum)                                                   |

Formato de cor: **RGBA** empacotado em `uint32` — `0xRRGGBBAA`

## Servidor Dev (Hot Reload)

`mabel dev` inicia um servidor hot reload estilo Expo:

1. Monitora arquivos `.razor`, `.cs`, `.css`, `.html` para mudancas
2. Recompila WASM quando detecta mudanca (debounce de 500ms)
3. Notifica clientes conectados via WebSocket
4. Serve o modulo WASM compilado via HTTP

Endpoints:
- `GET /mabel.wasm` — Modulo WASM compilado
- `GET /status` — JSON com versao do build, timestamp, contagem de clientes
- `WebSocket /ws` — Envia `reload:<versao>` ao recompilar

## Comecando

### Pre-requisitos

- Linux ou WSL2 (Ubuntu recomendado)
- Git

### 1. Clonar e configurar

```bash
git clone https://github.com/dmarquezbh/mabel-framework.git
cd mabel-framework
chmod +x setup.sh
./setup.sh
```

Isso instala:
- .NET 10 SDK
- Swift toolchain (para iOS)
- xtool (deploy para iOS a partir do Linux)
- wasmtime (runtime WASM)
- usbmuxd + libimobiledevice (USB iOS)
- adb (USB Android)

### 2. Verificar ambiente

```bash
# Garantir que dotnet esta no PATH
export PATH="$HOME/.dotnet:$PATH"

# Verificar se tudo esta instalado
dotnet run --project src/Mabel.Cli -- doctor
```

### 3. Compilar e testar

```bash
dotnet build
dotnet test
```

## Desenvolvimento iOS a partir do Linux (WSL2)

O Mabel suporta deploy para iPhones fisicos a partir do WSL2:

1. Conecte o iPhone via USB
2. Passe o dispositivo USB para o WSL: `usbipd attach --wsl --busid <busid>`
3. Reinicie o usbmuxd: `sudo systemctl restart usbmuxd`
4. Verifique: `idevice_id -l` deve mostrar o UDID do seu dispositivo
5. Execute `mabel usb-help` para instrucoes detalhadas passo a passo

## Testes

```bash
dotnet test                    # Executar todos os 21 testes
dotnet test --filter Renderer  # Executar apenas testes do renderer
dotnet test --filter Core      # Executar apenas testes do core
```

A infraestrutura de testes usa fakes (nao mocks):
- `FakeShellExecutor` — Registra comandos, retorna resultados configurados
- `FakeFileSystem` — File system em memoria
- `FakeCanvas` — Registra chamadas de desenho para asserções

## Stack Tecnologica

- **.NET 10** — SDK, CLI (AOT), componentes Blazor
- **WASM/WASI** — Formato binario portavel, sem necessidade de navegador
- **Swift** — Host nativo iOS (renderizacao Core Graphics)
- **SkiaSharp** — Renderizacao desktop (planejado)
- **xunit v3** — Framework de testes
- **xtool** — Deploy para iOS a partir do Linux

## Roadmap

- `mabel ai` — Geracao de UI via LLM (prompt-to-UI)
- WASI Component Model — Sistema de pacotes universal (pacotes de qualquer linguagem)
- Host Android (Kotlin + Canvas)
- Host Desktop (SkiaSharp)
- Projeto hello world de exemplo

## Contribuindo

Contribuicoes sao bem-vindas! Abra uma issue ou envie um pull request.

## Licenca

MIT
