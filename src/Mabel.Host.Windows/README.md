# Mabel.Host.Windows

Host **desktop Windows** do Mabel. Consome o **mesmo descritor SDUI**
(`SduiDocument` de `Mabel.Wasi.Protocol/Sdui/Descriptor.cs`) que o host iOS e o
guest WASM, e o mapeia para **controles nativos WPF** — sem canvas, sem
pixels. Scroll, hit-testing, foco de teclado e acessibilidade (UI Automation)
vêm do SO.

## Mapeamento SDUI → WPF (espelha `MabelSdui.swift`)

| Nó SDUI       | Controle nativo WPF                                  |
|---------------|------------------------------------------------------|
| `Screen`      | `Border` (background) + filho                        |
| `ScrollView`  | `ScrollViewer` (eixo por `axis`)                     |
| `VStack`/`HStack`/`List` | `Grid` (1 linha/coluna por filho; `flex>0` → `*`, senão `Auto`) |
| `Card`        | `Button` chromeless (clique nativo) + `Border`       |
| `Text`/`Button` | `TextBlock`                                        |
| `Badge`       | `Border` (pílula) + `TextBlock`                      |
| `ProgressBar` | `ProgressBar`                                        |
| `Divider`     | `Border` 1px                                         |

Cores em RGBA `0xRRGGBBAA` (mesmo formato do `RenderCommand`). `onTap` liga o
`Click` nativo do `Button` e devolve `{node.Id, action.Name, action.Args}`.

## Build (cross-compile no Linux/WSL) e run (Windows)

O `.exe` é gerado **no Linux** — o .NET cross-compila WPF via
`EnableWindowsTargeting` (baixa os targeting packs Windows Desktop do NuGet).
Não precisa de Windows nem toolchain especial para *buildar*; só para *rodar*
(runtime `Microsoft.WindowsDesktop.App`).

```bash
# no WSL/Linux:
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

```powershell
# no Windows:
.\Mabel.Host.Windows.exe            # abre a janela do Kanban
.\Mabel.Host.Windows.exe --selftest # headless: stats de render + simula tap em cada card
```

O descritor `assets/kanban-sdui.json` é o output do `board_gen` (guest).
