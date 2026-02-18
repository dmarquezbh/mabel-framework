# Tutorial: Seu Primeiro App Mabel

> [Read in English](TUTORIAL.md)

Este tutorial te guia na construcao do seu primeiro app Mabel — do zero ate
um frame de renderizacao funcionando. No final, voce vai entender o pipeline
de renderizacao e estar pronto para construir UIs reais.

## O Que Voce Vai Aprender

1. Como funciona o pipeline de renderizacao do Mabel
2. Como criar comandos de renderizacao
3. Como usar o `MabelRenderer` com um canvas
4. Como rodar o hello world de exemplo
5. Como funciona o hot reload do `mabel live`

## Pre-requisitos

- Linux ou WSL2 (Ubuntu recomendado)
- Git instalado

## Passo 1: Clonar e Configurar

```bash
git clone https://github.com/dmarquezbh/mabel-framework.git
cd mabel-framework
chmod +x setup.sh
./setup.sh
```

Apos a instalacao, garanta que o .NET esta no PATH:

```bash
export PATH="$HOME/.dotnet:$PATH"
```

> **Dica**: Adicione essa linha ao seu `~/.bashrc` para nao precisar digitar toda vez.

Verifique se tudo esta instalado:

```bash
dotnet run --project src/Mabel.Cli -- doctor
```

Voce deve ver checkmarks ao lado de todas as ferramentas.

## Passo 2: Compilar o Framework

```bash
dotnet build
dotnet test
```

Todos os 21 testes devem passar. Se nao passarem, rode `mabel setup` para
instalar dependencias faltantes.

## Passo 3: Rodar o Hello World

```bash
dotnet run --project samples/hello-world
```

Voce deve ver uma saida assim:

```
  Mabel Framework - Hello World
  ─────────────────────────────

  Screen: 390x844 (simulated iPhone 14)

=== Mabel Render Frame ===
  Commands: 16

  [BeginFrame  ] background=#1A1A2EFF
  [Rect        ] x=0 y=0 w=390 h=80 color=#6C63FFFF
  [Text        ] x=115 y=30 size=24 color=#FFFFFFFF "Mabel Framework"
  [Text        ] x=95 y=160 size=36 color=#FFFFFFFF "Hello, Mabel!"
  ...
  [EndFrame    ]

=== End Frame ===
```

Cada linha e um **comando de renderizacao** — o mesmo formato que e enviado
do seu modulo WASM para o host nativo via protocolo WASI.

## Passo 4: Entendendo o Pipeline de Renderizacao

A arquitetura do Mabel tem 4 camadas:

```
  Seu Codigo (Blazor/.razor)
       |
       | Compila para
       v
  Modulo WASM/WASI
       |
       | Envia RenderCommands via protocolo WASI
       v
  MabelRenderer (interpreta comandos)
       |
       | Chama metodos do ICanvas
       v
  Canvas Nativo (Core Graphics / SkiaSharp)
```

### RenderCommands

Cada elemento visual e um `RenderCommand` — uma struct plana com um `Op` (o que
desenhar) e parametros (posicao, tamanho, cor, texto):

```csharp
using Mabel.Wasi.Protocol;

// Desenhar um retangulo roxo
var rect = new RenderCommand(RenderOp.Rect, x: 10, y: 10, w: 200, h: 50, color: 0x6C63FFFF);

// Desenhar texto branco
var text = new RenderCommand(RenderOp.Text, x: 20, y: 25, 0, 0, color: 0xFFFFFFFF,
    Text: "Ola!", FontSize: 18);
```

### Formato de Cor

Cores sao RGBA empacotadas em `uint32`:

```
0xRRGGBBAA

Exemplos:
  0xFFFFFFFF = branco (opacidade total)
  0x000000FF = preto (opacidade total)
  0xFF000080 = vermelho (50% opacidade)
  0x6C63FFFF = roxo (opacidade total)
```

### Operacoes Disponiveis

| Op | O que desenha |
|----|---------------|
| `BeginFrame` | Inicia um novo frame, define cor de fundo |
| `Rect` | Retangulo preenchido |
| `RoundRect` | Retangulo arredondado |
| `Circle` | Circulo preenchido |
| `Line` | Linha entre dois pontos |
| `Text` | String de texto |
| `Image` | Imagem por ID |
| `PushClip` / `PopClip` | Limita o desenho a um retangulo |
| `PushOpacity` / `PopOpacity` | Define opacidade para desenhos seguintes |
| `Translate` | Move a origem das coordenadas |
| `EndFrame` | Finaliza o frame |

### MabelRenderer

O `MabelRenderer` recebe uma lista de comandos e desenha em um `ICanvas`:

```csharp
using Mabel.Renderer;
using Mabel.Wasi.Protocol;

// O canvas e fornecido pela plataforma:
//   iOS:     MabelCanvasView (Core Graphics)
//   Desktop: SkiaSharpCanvas (planejado)
//   Testes:  FakeCanvas
ICanvas canvas = GetPlatformCanvas();

var renderer = new MabelRenderer(canvas);
renderer.Render(commands);
```

O renderer gerencia `SaveState`/`RestoreState` automaticamente — cada frame
comeca com estado salvo e restaura no final, entao transforms (Translate)
nao vazam entre frames.

## Passo 5: Modificar o Hello World

Abra `samples/hello-world/HelloApp.cs` e tente mudar coisas:

### Mudar a cor de fundo

```csharp
// Linha no BuildFrame — mude DarkBlue para qualquer cor
commands.Add(Frame(RenderOp.BeginFrame, 0x2D2D44FF)); // cinza-azulado escuro
```

### Adicionar um novo elemento

```csharp
// Adicione apos o texto do footer
commands.Add(RoundRect(40, 660, screenWidth - 80, 80, 0x4CAF50FF, 12)); // card verde
commands.Add(Text(60, 685, "Feito com .NET 10", 16, White));
```

### Rode novamente para ver as mudancas

```bash
dotnet run --project samples/hello-world
```

## Passo 6: Como Funciona o Mabel Live

Quando voce esta desenvolvendo um app Mabel real (nao apenas o sample), use
`mabel live` para hot reload:

```bash
dotnet run --project src/Mabel.Cli -- live [caminho-do-projeto]
```

Isso inicia o servidor **Mabel Live**:

1. **Compila** seu projeto Blazor para WASM
2. **Monitora** mudancas em arquivos (`.razor`, `.cs`, `.css`, `.html`)
3. **Recompila** automaticamente quando voce salva (debounce de 500ms)
4. **Notifica** dispositivos conectados via WebSocket
5. O dispositivo **baixa** o novo WASM e re-renderiza instantaneamente

```
  [live] Building WASM...
  [live] Build OK
  [live] Watching for file changes...
  [live] Dev server running on http://192.168.1.100:5555
    WASM:      http://192.168.1.100:5555/mabel.wasm
    WebSocket: ws://192.168.1.100:5555/ws
    Status:    http://192.168.1.100:5555/status

  [live] File changed — rebuilding...
  [live] Build OK (v2)
  [live] Notified 1 client(s)
```

Opcoes:
- `--port, -P` — Mudar a porta (padrao: 5555)
- `--verbose` — Mostrar logs detalhados

## Passo 7: Criar um Novo Projeto

Para criar o scaffold de um projeto Mabel completo com hosts nativos:

```bash
dotnet run --project src/Mabel.Cli -- create meu-app --platform ios
```

Isso gera:

```
meu-app/
  mabel.json           # Manifesto do projeto
  web_app/             # Projeto Blazor WASM (seu codigo de UI)
  ios_app/             # Host nativo iOS (Swift Package)
    Package.swift
    xtool.yml
    Sources/ios_app/
      ContentView.swift
```

## Proximos Passos

- **Deploy no iPhone**: Conecte via USB e rode `mabel deploy`
- **Mais elementos de UI**: Use todas as operacoes `RenderOp`
- **APIs nativas**: WASI Capability Providers para camera, GPS, etc. (em breve)
- **Mabel Live**: Hot reload em tempo real durante o desenvolvimento

## Mergulho na Arquitetura

Para mais detalhes sobre a arquitetura, veja:
- [AGENT.md](../AGENT.md) — Guia de desenvolvimento com convencoes
- [PHASE2.md](PHASE2.md) — Roadmap da Fase 2 (playground, IA, GitHub Pages)
- Codigo fonte em `src/Mabel.Wasi.Protocol/Protocol.cs` — Documentacao completa do protocolo
