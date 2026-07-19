using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Host.Windows;

// =============================================================================
// Entry point do host Windows. Le o descritor SDUI (kanban-sdui.json emitido
// pelo board_gen), constroi a arvore de controles NATIVOS WPF e abre a janela.
//
//   Mabel.Host.Windows.exe            -> abre a janela do Kanban (GUI).
//   Mabel.Host.Windows.exe --selftest -> headless: mede/arranja a arvore,
//                                         imprime stats de render e simula o
//                                         tap em cada card (evidencia sem GUI).
// =============================================================================
public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [STAThread]
    public static int Main(string[] args)
    {
        bool selftest = args.Contains("--selftest");

        var jsonPath = LocateDescriptor(args);
        if (jsonPath is null)
        {
            Console.Error.WriteLine("kanban-sdui.json nao encontrado (assets/ ao lado do exe).");
            return 2;
        }

        var json = File.ReadAllText(jsonPath);
        var doc = JsonSerializer.Deserialize<SduiDocument>(json, JsonOpts);
        if (doc is null)
        {
            Console.Error.WriteLine("Falha ao desserializar SduiDocument.");
            return 3;
        }

        Console.WriteLine($"SDUI schemaVersion={doc.SchemaVersion}  root={doc.Root.Id} ({doc.Root.Type})");

        var app = new Application();
        var builder = new MabelWindowsBuilder();

        // Barra de status: mostra o ultimo tap resolvido (id + acao).
        var status = new TextBlock
        {
            Text = "Mabel Host Windows - SDUI -> controles nativos WPF. Clique num card.",
            Margin = new Thickness(12, 8, 12, 8),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
        };
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xE0, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = status,
        };

        int tapCount = 0;
        builder.OnAction = (node, action) =>
        {
            tapCount++;
            var argsStr = action.Args is null ? "" :
                "{" + string.Join(", ", action.Args.Select(kv => $"{kv.Key}={kv.Value}")) + "}";
            var credor = node.Props?.Data is { } d && d.TryGetValue("credor", out var cr) ? cr : "";
            var line = $"tap #{tapCount}: {node.Id} -> {action.Name} {argsStr}  [{credor}]";
            Console.WriteLine("[tap] " + line);
            status.Text = line;
        };

        var root = builder.Build(doc);

        var layout = new DockPanel();
        DockPanel.SetDock(statusBar, Dock.Top);
        layout.Children.Add(statusBar);
        layout.Children.Add(root); // preenche o resto

        var win = new Window
        {
            Title = "Mabel Host Windows - Kanban (SDUI nativo)",
            Width = 1200,
            Height = 820,
            Content = layout,
            Background = Brushes.White,
        };

        if (selftest)
            return SelfTest(builder, layout, root);

        app.Run(win);
        return 0;
    }

    // Headless: forca layout (measure/arrange) sem abrir janela, imprime as
    // estatisticas de render e simula o tap em cada botao nativo.
    private static int SelfTest(MabelWindowsBuilder builder, FrameworkElement layout, FrameworkElement root)
    {
        Console.WriteLine("=== SELFTEST (headless) ===");
        var size = new Size(1200, 820);
        layout.Measure(size);
        layout.Arrange(new Rect(size));
        layout.UpdateLayout();

        Console.WriteLine($"nos totais    : {builder.NodeCount}");
        Console.WriteLine("por tipo      :");
        foreach (var kv in builder.Counts.OrderBy(k => k.Key.ToString()))
            Console.WriteLine($"  {kv.Key,-12}: {kv.Value}");

        Console.WriteLine($"root arranjado: {root.ActualWidth:0}x{root.ActualHeight:0} px");
        bool laidOut = root.ActualWidth > 0 && root.ActualHeight > 0;
        Console.WriteLine($"layout ok     : {laidOut}");

        Console.WriteLine($"botoes de tap : {builder.TapButtons.Count}");
        Console.WriteLine("--- simulando tap em cada card ---");
        foreach (var btn in builder.TapButtons)
            btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        bool ok = laidOut && builder.TapButtons.Count > 0;
        Console.WriteLine(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
        return ok ? 0 : 1;
    }

    private static string? LocateDescriptor(string[] args)
    {
        foreach (var a in args)
            if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                return a;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "kanban-sdui.json"),
            Path.Combine(AppContext.BaseDirectory, "kanban-sdui.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
