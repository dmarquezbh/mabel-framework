using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mabel.Wasi.Protocol.DevTools;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Host.Windows;

// =============================================================================
// Entry point do host Windows. Le o descritor SDUI, constroi a arvore de
// controles NATIVOS WPF e abre a janela.
//
//   Mabel.Host.Windows.exe                -> Kanban v1 (assets/kanban-sdui.json).
//   Mabel.Host.Windows.exe --onda2        -> descritor v2 (assets/kanban-onda2.json):
//                                            NavStack + List virtualizada de 30 +
//                                            no tipo-200 (fallback) + a11y + responsivo.
//   Mabel.Host.Windows.exe <path.json>    -> descritor arbitrario.
//   ... --selftest                        -> headless: stats + valida list/nav/fallback.
//
// Desserializa com SduiJson (contrato canonico: camelCase, enums numericos,
// omite null) — o MESMO usado pelo emissor e pelo host iOS.
// =============================================================================
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool selftest = args.Contains("--selftest");
        bool onda2 = args.Contains("--onda2");
        bool onda3 = args.Contains("--onda3");
        bool dark = args.Contains("--dark");
        bool inspect = args.Contains("--inspect");
        bool inspectJson = args.Contains("--inspect-json");
        string? locale = LocaleArg(args);

        var jsonPath = LocateDescriptor(args, onda2, onda3);
        if (jsonPath is null)
        {
            Console.Error.WriteLine("descritor .json nao encontrado (assets/ ao lado do exe).");
            return 2;
        }

        var json = File.ReadAllText(jsonPath);
        var doc = SduiJson.Deserialize(json);
        if (doc is null)
        {
            Console.Error.WriteLine("Falha ao desserializar SduiDocument.");
            return 3;
        }

        Console.WriteLine($"descritor: {Path.GetFileName(jsonPath)}");
        Console.WriteLine($"SDUI schemaVersion={doc.SchemaVersion}  host suporta v{SduiSchema.CurrentVersion}  root={doc.Root.Id} ({doc.Root.Type})");

        // ── Onda 🟢: DevTools — dump da árvore SDUI (inspector) ──────────────
        // Headless: resolve tema/locale ativos e imprime a árvore navegável (texto
        // ou JSON) sem abrir janela WPF. Espelha o que o host renderiza ao vivo.
        if (inspect || inspectJson)
        {
            var inspectOpts = new SduiInspectorOptions
            {
                ThemeMode = dark ? SduiThemeMode.Dark : (doc.ThemeMode ?? SduiThemeMode.System),
                SystemPrefersDark = dark,
                Locale = locale ?? doc.Localization?.DefaultLocale,
            };
            Console.WriteLine(inspectJson
                ? SduiInspector.ToJson(doc, inspectOpts)
                : SduiInspector.ToText(doc, inspectOpts));
            return 0;
        }

        var app = new Application();
        var builder = new MabelWindowsBuilder();
        // Onda 🟡: tema (claro/escuro) + locale ativos (via flags de CLI).
        builder.SetDarkMode(dark);
        if (locale is not null) builder.SetLocale(locale);
        builder.OnLifecycle = (node, action) =>
            Console.WriteLine($"[lifecycle] {node.Id} -> {action.Name}");
        // Onda 🟢: error boundaries — telemetria de subárvores isoladas por falha.
        builder.ErrorSink = new SduiDelegateErrorSink(e =>
            Console.WriteLine($"[telemetry] error node={e.NodeId} type={e.TypeCode} kind={e.Kind}: {e.Message}"));
        // selftest/screenshot são renders estáticos (sem clock de animação).
        builder.StaticRender = selftest || ShotPath(args) is not null;

        var status = new TextBlock
        {
            Text = $"Mabel Host Windows - SDUI v{doc.SchemaVersion} -> controles nativos WPF.",
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
            var navStr = action.Navigate is { } n ? $"  nav={n.Kind}:{n.Route ?? "-"}" : "";
            var line = $"tap #{tapCount}: {node.Id} -> {action.Name} {argsStr}{navStr}";
            Console.WriteLine("[tap] " + line);
            status.Text = line;
        };

        var root = builder.Build(doc);

        var layout = new DockPanel();
        DockPanel.SetDock(statusBar, Dock.Top);
        layout.Children.Add(statusBar);
        layout.Children.Add(root);

        var win = new Window
        {
            Title = $"Mabel Host Windows - SDUI v{doc.SchemaVersion}" + (onda2 ? " (Onda 2)" : ""),
            Width = 1200, Height = 820,
            Content = layout,
            Background = Brushes.White,
        };

        if (selftest)
            return SelfTest(builder, layout, root, doc);

        // --shot <path>: renderiza a árvore de controles nativos WPF pra PNG via
        // RenderTargetBitmap (o próprio compositor do WPF) — screenshot determinístico
        // do visual real, sem depender de janela em foco / z-order / desktop ativo.
        var shotPath = ShotPath(args);
        if (shotPath is not null)
            return Shot(layout, shotPath, 1200, 820);

        app.Run(win);
        return 0;
    }

    private static int SelfTest(MabelWindowsBuilder builder, FrameworkElement layout, FrameworkElement root, SduiDocument doc)
    {
        Console.WriteLine("=== SELFTEST (headless) ===");
        var size = new Size(1200, 820);
        layout.Measure(size);
        layout.Arrange(new Rect(size));
        layout.UpdateLayout();

        Console.WriteLine($"nos totais    : {builder.NodeCount}");
        Console.WriteLine("por tipo      :");
        foreach (var kv in builder.Counts.OrderBy(k => (byte)k.Key))
            Console.WriteLine($"  {(byte)kv.Key,3} {kv.Key,-12}: {kv.Value}");

        Console.WriteLine($"root arranjado: {root.ActualWidth:0}x{root.ActualHeight:0} px");
        bool laidOut = root.ActualWidth > 0 && root.ActualHeight > 0;

        // ── a11y / responsivo ────────────────────────────────────────────────
        Console.WriteLine($"a11y aplicado : {builder.A11yApplied} nós");
        Console.WriteLine($"responsivo    : {builder.ResponsiveApplied} override(s) de fontSize aplicados (size-class Regular @1200px)");

        // ── Fallback (tipo desconhecido 200) ─────────────────────────────────
        bool fallbackOk = builder.FallbackPlaceholders >= 1;
        Console.WriteLine($"fallback chip : placeholders={builder.FallbackPlaceholders} texto=\"{builder.PlaceholderText}\"  -> {(fallbackOk ? "OK" : "FALTOU")}");

        // ── List virtualizada de 30 ──────────────────────────────────────────
        bool listOk = false;
        if (builder.LastList is { } lb)
        {
            lb.UpdateLayout();
            int realized0 = CountVisual<ListBoxItem>(lb);
            // rola até o último item -> prova que a janela de 30 está toda acessível.
            if (lb.Items.Count > 0) lb.ScrollIntoView(lb.Items[^1]);
            lb.UpdateLayout();
            int realizedLast = CountVisual<ListBoxItem>(lb);
            var lastContainer = lb.ItemContainerGenerator.ContainerFromIndex(lb.Items.Count - 1);
            listOk = builder.LastListLogicalCount == 30 && lb.Items.Count == 30 && realized0 > 0 && lastContainer is not null;
            Console.WriteLine($"list virtual. : logico={builder.LastListLogicalCount} itens={lb.Items.Count} realizados(topo)={realized0} realizados(fim)={realizedLast} ultimo-container={(lastContainer is not null)}");
            Console.WriteLine($"                (virtualizado: só ~{realized0}/30 materializados por vez -> reciclagem OK)");
            var sample = FirstText(lb);
            if (sample is not null) Console.WriteLine($"                1a linha bound: \"{sample}\"");
        }
        else Console.WriteLine("list virtual. : NENHUMA List virtualizada encontrada");

        // ── NavStack push/pop ────────────────────────────────────────────────
        bool navOk = false;
        if (builder.Nav is { } nav)
        {
            Console.WriteLine($"nav inicial   : route={nav.CurrentRoute} title=\"{nav.CurrentTitle}\" depth={nav.Depth}");
            nav.Apply(new SduiNavigate { Kind = SduiNavKind.Push, Route = "detail" });
            layout.UpdateLayout();
            Console.WriteLine($"nav push      : route={nav.CurrentRoute} title=\"{nav.CurrentTitle}\" depth={nav.Depth}");
            bool pushed = nav.CurrentRoute == "detail" && nav.Depth == 2;
            nav.Apply(new SduiNavigate { Kind = SduiNavKind.Pop });
            layout.UpdateLayout();
            Console.WriteLine($"nav pop       : route={nav.CurrentRoute} title=\"{nav.CurrentTitle}\" depth={nav.Depth}");
            bool popped = nav.CurrentRoute == "home" && nav.Depth == 1;
            navOk = pushed && popped;
        }
        else Console.WriteLine("nav           : NENHUM NavStack (descritor v1?)");

        // ── Taps ─────────────────────────────────────────────────────────────
        Console.WriteLine($"botoes de tap : {builder.TapButtons.Count}");

        // ── Onda 🟡 (v3): theming + i18n + forms + catálogo ────────────────────
        bool v3 = doc.SchemaVersion >= 3;
        Console.WriteLine($"theming       : {builder.ThemeTokensResolved} token(s) de cor resolvidos (dark={builder.PrefersDark})");
        Console.WriteLine($"i18n          : {builder.I18nResolved} texto(s) localizados");
        Console.WriteLine($"forms         : {builder.InputsBuilt} input(s) [{string.Join(", ", builder.Inputs.Keys)}]");
        Console.WriteLine($"animacoes     : {builder.AnimationsApplied} declaradas");
        Console.WriteLine($"lifecycle     : {builder.LifecycleHooks} hook(s) onAppear/onDisappear");
        Console.WriteLine($"err-boundary  : {builder.ErrorBoundariesTriggered} subárvore(s) isolada(s) por falha de render");
        bool v3ok = !v3 || (builder.ThemeTokensResolved > 0 && builder.I18nResolved > 0 && builder.InputsBuilt > 0);
        if (v3) Console.WriteLine($"v3 funcional  : {(v3ok ? "OK" : "FALTOU (theming/i18n/forms)")}");

        // Estruturais: só exigidos quando o construto está presente no descritor.
        bool structOk = laidOut
            && (builder.Nav is null || navOk)
            && (builder.LastList is null || listOk);
        // Cenário v2 (onda2): exige o pacote completo fallback+list+nav.
        bool v2Scenario = doc.SchemaVersion == 2;
        bool ok = structOk && v3ok && (!v2Scenario || (fallbackOk && listOk && navOk));
        Console.WriteLine(ok ? "SELFTEST PASS" : "SELFTEST FAIL");
        return ok ? 0 : 1;
    }

    private static string? ShotPath(string[] args)
    {
        int i = Array.IndexOf(args, "--shot");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Shot(FrameworkElement layout, string path, int w, int h)
    {
        var size = new Size(w, h);
        layout.Measure(size);
        layout.Arrange(new Rect(size));
        layout.UpdateLayout();

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(layout);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        Console.WriteLine($"shot salvo: {path} ({w}x{h})");
        return 0;
    }

    private static string? FirstText(DependencyObject root)
    {
        if (root is TextBlock { Text.Length: > 0 } tb) return tb.Text;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var r = FirstText(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (r is not null) return r;
        }
        return null;
    }

    private static int CountVisual<T>(DependencyObject root) where T : DependencyObject
    {
        int count = 0;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T) count++;
            count += CountVisual<T>(child);
        }
        return count;
    }

    private static string? LocaleArg(string[] args)
    {
        int i = Array.IndexOf(args, "--locale");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? LocateDescriptor(string[] args, bool onda2, bool onda3)
    {
        foreach (var a in args)
            if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(a))
                return a;

        var name = onda3 ? "kanban-onda3.json" : onda2 ? "kanban-onda2.json" : "kanban-sdui.json";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", name),
            Path.Combine(AppContext.BaseDirectory, name),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
