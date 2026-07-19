using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Host.Windows;

// =============================================================================
// Mabel SDUI - host Windows (WPF) - schema v2 (Onda 2)
//
// Consome o MESMO SduiDocument que o host iOS e o guest WASM e o mapeia pra
// controles NATIVOS WPF. Onda 2 adiciona:
//   - decode TOLERANTE / degradação graciosa: Type desconhecido (ou
//     MinSchemaVersion > host) -> SduiUnknownFallback (renderChildren /
//     placeholder chip amarelo / ignore). Espelha ResolveFallback do contrato.
//   - List VIRTUALIZADA: ListBox com VirtualizingStackPanel (reciclagem),
//     ItemTemplate + Bind por linha (template + dados, sem materializar N nós).
//   - NavStack: navegação nativa push/pop/replace/root/popTo (nav bar + back).
//   - a11y: AutomationProperties (Name/HelpText/HeadingLevel) a partir de A11y.
//   - responsivo: min/max + override de Props por size-class/breakpoint.
// =============================================================================
public sealed class MabelWindowsBuilder
{
    private readonly int _hostSchema = SduiSchema.CurrentVersion;
    // Largura de container assumida (desktop) — decide a size-class responsiva.
    private double _containerWidth = 1200;
    private SduiSizeClass SizeClass => _containerWidth >= 700 ? SduiSizeClass.Regular : SduiSizeClass.Compact;

    /// Contexto de binding da linha corrente (List virtualizada). Setado por linha.
    private IReadOnlyDictionary<string, string>? _row;

    // ── Onda 🟡: theming + i18n ──────────────────────────────────────────────
    // Resolvedores puros do contrato (Theming.cs / Localization.cs). Inicializados
    // a partir do documento (tema ativo + locale). Nós resolvem cores/textos por
    // token/chave; sem tokens/chaves, o comportamento é idêntico ao da v2.
    private SduiThemeResolver _theme = new(null, SduiThemeMode.System);
    private SduiLocalizer _l10n = new(null, null);
    private bool _prefersDark;
    private string? _locale;

    /// Callback disparado quando um nó clicável (onTap) é acionado.
    public Action<SduiNode, SduiAction>? OnAction { get; set; }

    /// Callback de lifecycle (onAppear/onDisappear). Ligado ao Loaded/Unloaded nativo.
    public Action<SduiNode, SduiAction>? OnLifecycle { get; set; }

    /// Preferência de tema escuro do SO/usuário (afeta a resolução de tokens).
    public void SetDarkMode(bool dark) => _prefersDark = dark;
    /// Preferência de tema escuro ativa (telemetria).
    public bool PrefersDark => _prefersDark;

    /// Render estático (selftest/screenshot): as animações de aparição são
    /// contadas mas NÃO escondem o conteúdo (não há clock pra revelá-lo). Numa
    /// janela viva (app.Run) fica false → as animações rodam de verdade.
    public bool StaticRender { get; set; }
    /// Locale ativo (i18n). Ex.: "pt-BR", "en".
    public void SetLocale(string? locale) => _locale = locale;

    // ── Telemetria de render (evidência do spike) ───────────────────────────
    public readonly Dictionary<SduiNodeType, int> Counts = new();
    public readonly List<Button> TapButtons = new();
    public int NodeCount { get; private set; }
    public int FallbackPlaceholders { get; private set; }
    public int FallbackRenderChildren { get; private set; }
    public int A11yApplied { get; private set; }
    public int ResponsiveApplied { get; private set; }
    public string? PlaceholderText { get; private set; }

    // ── Onda 🟡: telemetria funcional ─────────────────────────────────────────
    public int ThemeTokensResolved { get; private set; }
    public int I18nResolved { get; private set; }
    public int InputsBuilt { get; private set; }
    public int AnimationsApplied { get; private set; }
    public int LifecycleHooks { get; private set; }
    /// Inputs construídos, por Field → controle nativo (p/ --selftest inspecionar).
    public readonly Dictionary<string, FrameworkElement> Inputs = new();

    /// Última List virtualizada instanciada + total lógico (p/ --selftest).
    public ListBox? LastList { get; private set; }
    public int LastListLogicalCount { get; private set; }

    /// NavStack ativo (p/ --selftest simular push/pop).
    public NavHost? Nav { get; private set; }

    public void SetContainerWidth(double w) => _containerWidth = w;

    public FrameworkElement Build(SduiDocument doc)
    {
        // Onda 🟡: prepara tema ativo + locale a partir do documento.
        _theme = new SduiThemeResolver(doc.Themes, doc.ThemeMode ?? SduiThemeMode.System, _prefersDark);
        _l10n = new SduiLocalizer(doc.Localization, _locale ?? doc.Localization?.DefaultLocale);
        return Build(doc.Root);
    }

    private FrameworkElement Build(SduiNode node)
    {
        NodeCount++;
        Counts[node.Type] = Counts.TryGetValue(node.Type, out var c) ? c + 1 : 1;

        // ── Degradação graciosa: nó não reconhecido pelo host? ───────────────
        if (node.ResolveFallback(_hostSchema) is { } policy)
            return Decorated(node, BuildFallback(node, policy));

        FrameworkElement element = node.Type switch
        {
            SduiNodeType.Screen      => Box(node.Props, BuildFirstChild(node)),
            SduiNodeType.ScrollView  => BuildScroll(node),
            SduiNodeType.VStack      => Box(node.Props, BuildStack(node, Orientation.Vertical)),
            SduiNodeType.HStack      => Box(node.Props, BuildStack(node, Orientation.Horizontal)),
            SduiNodeType.List when node.List is not null => BuildVirtualizedList(node, node.List),
            SduiNodeType.List        => Box(node.Props, BuildStack(node,
                                            node.Props?.Axis == SduiAxis.Horizontal
                                                ? Orientation.Horizontal : Orientation.Vertical)),
            SduiNodeType.Card        => BuildCard(node),
            SduiNodeType.Text        => Box(node.Props, BuildText(node)),
            SduiNodeType.Button      => Box(node.Props, BuildText(node)),
            SduiNodeType.Badge       => BuildBadge(node),
            SduiNodeType.ProgressBar => BuildProgress(node),
            SduiNodeType.Divider     => BuildDivider(node),
            SduiNodeType.NavStack    => BuildNavStack(node),
            SduiNodeType.Image       => new Border(),
            SduiNodeType.Spacer      => new Grid(),

            // ── Onda 🟡 (funcional) ──────────────────────────────────────────
            SduiNodeType.TextField   => BuildTextField(node),
            SduiNodeType.Select      => BuildSelect(node),
            SduiNodeType.Checkbox    => BuildCheckbox(node),
            SduiNodeType.Switch      => BuildSwitch(node),
            SduiNodeType.Slider      => BuildSlider(node),
            SduiNodeType.Stepper     => BuildStepper(node),
            SduiNodeType.TabBar      => BuildTabBar(node),
            SduiNodeType.Grid        => Box(node.Props, BuildGrid(node)),
            SduiNodeType.Sheet       => BuildSheet(node),
            SduiNodeType.Avatar      => BuildAvatar(node),
            SduiNodeType.Chip        => BuildChip(node),
            SduiNodeType.Video       => BuildMediaPlaceholder(node, "▶ vídeo"),
            SduiNodeType.Audio       => BuildMediaPlaceholder(node, "♪ áudio"),

            _                        => new Grid(),
        };

        return Decorated(node, element);
    }

    /// Aplica size/constraints, a11y e clicabilidade — comum a todo nó.
    private FrameworkElement Decorated(SduiNode node, FrameworkElement element)
    {
        ApplySize(node.Props, element);
        ApplyConstraints(node.Props, element);
        ApplyA11y(node, element);
        ApplyAnimation(node, element);
        ApplyLifecycle(node, element);
        if (node.OnTap is { } action)
            element = MakeClickable(element, node, action);
        return element;
    }

    // ── Onda 🟡: animação (fade/scale/slide/expand) ──────────────────────────
    private void ApplyAnimation(SduiNode node, FrameworkElement el)
    {
        if (node.Animation is not { } anim || anim.Kind == SduiAnimationKind.None) return;
        AnimationsApplied++;
        // Render estático: conta a animação mas mantém o conteúdo visível.
        if (StaticRender) return;
        double dur = (anim.DurationMs ?? 250) / 1000.0;
        var duration = new System.Windows.Duration(TimeSpan.FromSeconds(dur));
        var begin = TimeSpan.FromMilliseconds(anim.DelayMs ?? 0);

        // OnAppear (default): dispara no Loaded. OnTap/Continuous ficam como intenção
        // declarada (contadas), sem timeline aqui — o foco é validar o mapeamento.
        if ((anim.Trigger ?? SduiAnimationTrigger.OnAppear) != SduiAnimationTrigger.OnAppear) return;

        switch (anim.Kind)
        {
            case SduiAnimationKind.Fade:
                el.Opacity = 0;
                el.Loaded += (_, _) => el.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { BeginTime = begin });
                break;
            case SduiAnimationKind.Scale:
                var st = new ScaleTransform(0.8, 0.8);
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = st;
                el.Loaded += (_, _) =>
                {
                    var a = new System.Windows.Media.Animation.DoubleAnimation(0.8, 1, duration) { BeginTime = begin };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                };
                break;
            default: // Slide/Expand: fade como aproximação neutra (sem quebrar layout headless).
                el.Opacity = 0;
                el.Loaded += (_, _) => el.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { BeginTime = begin });
                break;
        }
    }

    // ── Onda 🟡: lifecycle (onAppear/onDisappear) ────────────────────────────
    private void ApplyLifecycle(SduiNode node, FrameworkElement el)
    {
        if (node.OnAppear is { } appear)
        {
            LifecycleHooks++;
            el.Loaded += (_, _) => OnLifecycle?.Invoke(node, appear);
        }
        if (node.OnDisappear is { } disappear)
        {
            LifecycleHooks++;
            el.Unloaded += (_, _) => OnLifecycle?.Invoke(node, disappear);
        }
    }

    private FrameworkElement BuildFirstChild(SduiNode node)
    {
        var child = node.Children?.FirstOrDefault();
        return child is null ? new Grid() : Build(child);
    }

    // ── Degradação graciosa ──────────────────────────────────────────────────
    private FrameworkElement BuildFallback(SduiNode node, SduiUnknownFallback policy)
    {
        var raw = (byte)node.Type;
        Console.WriteLine($"[fallback] node={node.Id} typeRaw={raw} policy={policy}");
        switch (policy)
        {
            case SduiUnknownFallback.Placeholder:
                FallbackPlaceholders++;
                PlaceholderText = $"⚠ nó não suportado ({raw})";
                var tb = new TextBlock
                {
                    Text = PlaceholderText,
                    FontSize = 12,
                    Foreground = Brush(0x9A6B00FF),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                return new Border
                {
                    Child = tb,
                    Background = Brush(0xFFF8E1FF),
                    BorderBrush = Brush(0xF0D98CFF),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };

            case SduiUnknownFallback.Ignore:
                return new Grid { Width = 0, Height = 0 };

            case SduiUnknownFallback.RenderChildren:
            default:
                FallbackRenderChildren++;
                return BuildStack(node, Orientation.Vertical); // container transparente
        }
    }

    private ScrollViewer BuildScroll(SduiNode node)
    {
        bool horizontal = node.Props?.Axis == SduiAxis.Horizontal;
        var sv = new ScrollViewer
        {
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            Content = BuildFirstChild(node),
        };
        if (node.Props?.Padding is { } p)
            sv.Padding = new Thickness(p.Left, p.Top, p.Right, p.Bottom);
        if (node.Props?.Background is { } bg) sv.Background = Brush(bg);
        return sv;
    }

    private Grid BuildStack(SduiNode node, Orientation orientation)
    {
        var grid = new Grid();
        var children = node.Children ?? Array.Empty<SduiNode>();
        double spacing = node.Props?.Spacing ?? 0;
        var align = node.Props?.Align ?? SduiAlign.Stretch;

        for (int i = 0; i < children.Count; i++)
        {
            var childNode = children[i];
            var child = Build(childNode);

            // flex-grow: explícito, senão Flex, senão uma List virtualizada
            // preenche o eixo (espelha isFillingList do host iOS).
            bool isFillingList = childNode.Type == SduiNodeType.List && childNode.List is not null;
            double grow = childNode.Props?.FlexGrow ?? childNode.Props?.Flex ?? (isFillingList ? 1 : 0);
            bool flex = grow > 0;

            if (orientation == Orientation.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = flex ? new GridLength(grow, GridUnitType.Star) : GridLength.Auto,
                });
                Grid.SetColumn(child, i);
                child.VerticalAlignment = CrossV(align);
                if (i > 0) AddLeftMargin(child, spacing);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = flex ? new GridLength(grow, GridUnitType.Star) : GridLength.Auto,
                });
                Grid.SetRow(child, i);
                child.HorizontalAlignment = CrossH(align);
                if (i > 0) AddTopMargin(child, spacing);
            }
            grid.Children.Add(child);
        }
        return grid;
    }

    private FrameworkElement BuildCard(SduiNode node)
        => Box(node.Props, BuildStack(node, Orientation.Vertical));

    private TextBlock BuildText(SduiNode node)
    {
        // Onda 🟡: token de tipografia (TextStyle) provê defaults; FontSize/Weight
        // explícitos do nó vencem. Cor resolve token de tema → cor crua.
        var style = _theme.TextStyle(node.Props?.TextStyle);
        double fs = node.Props?.FontSize ?? style?.FontSize ?? 14;
        fs = ApplyResponsiveFontSize(node, fs);
        var weight = node.Props?.Weight ?? style?.Weight;
        var tb = new TextBlock
        {
            Text = ResolveText(node),
            FontSize = fs,
            FontWeight = Weight(weight),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (ResolveColor(node.Props) is { } col) tb.Foreground = Brush(col);
        else if (_theme.Color(style?.ColorToken) is { } sc) tb.Foreground = Brush(sc);
        else if (style?.Color is { } rawc) tb.Foreground = Brush(rawc);
        return tb;
    }

    // ── Onda 🟡: resolução de cor via tema (token → cru) ─────────────────────
    private uint? ResolveColor(SduiProps? p)
    {
        if (p?.ColorToken is not null && _theme.Color(p.ColorToken) is { } c) { ThemeTokensResolved++; return c; }
        return p?.Color;
    }
    private uint? ResolveBackground(SduiProps? p)
    {
        if (p?.BackgroundToken is not null && _theme.Color(p.BackgroundToken) is { } c) { ThemeTokensResolved++; return c; }
        return p?.Background;
    }
    private uint? ResolveBorderColor(SduiProps? p)
    {
        if (p?.BorderColorToken is not null && _theme.Color(p.BorderColorToken) is { } c) { ThemeTokensResolved++; return c; }
        return p?.BorderColor;
    }

    private Border BuildBadge(SduiNode node)
    {
        var tb = new TextBlock
        {
            Text = ResolveText(node),
            FontSize = node.Props?.FontSize ?? 10,
            FontWeight = Weight(node.Props?.Weight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (ResolveColor(node.Props) is { } col) tb.Foreground = Brush(col);
        var pill = new Border
        {
            Child = tb,
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (ResolveBackground(node.Props) is { } bg) pill.Background = Brush(bg);
        if (node.Props?.CornerRadius is { } cr) pill.CornerRadius = new CornerRadius(cr);
        return pill;
    }

    private ProgressBar BuildProgress(SduiNode node)
    {
        var pb = new ProgressBar
        {
            Minimum = 0, Maximum = 1,
            Value = node.Props?.Value ?? 0,
            Height = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            BorderThickness = new Thickness(0),
        };
        if (ResolveColor(node.Props) is { } col) pb.Foreground = Brush(col);
        return pb;
    }

    private static Border BuildDivider(SduiNode node)
    {
        var b = new Border { Height = 1 };
        b.Background = node.Props?.Background is { } bg ? Brush(bg) : new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        return b;
    }

    // =========================================================================
    // Onda 🟡 (funcional): inputs de formulário + catálogo ampliado + media.
    // =========================================================================

    /// Envolve um input num painel vertical com rótulo de erro (estado de validação).
    private FrameworkElement WithField(SduiNode node, FrameworkElement input)
    {
        if (node.Props?.Field is { } field)
        {
            InputsBuilt++;
            Inputs[field] = input;
        }
        if (node.Props?.Disabled == true && input is Control ctl) ctl.IsEnabled = false;
        return input;
    }

    private FrameworkElement BuildTextField(SduiNode node)
    {
        var placeholder = _l10n.ResolvePlaceholder(node.Props);
        FrameworkElement input;
        if (node.Props?.Secure == true)
        {
            input = new PasswordBox { Padding = new Thickness(6, 4, 6, 4) };
        }
        else
        {
            var tb = new TextBox
            {
                Text = node.Props?.DefaultValue ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                AcceptsReturn = node.Props?.Multiline == true,
                TextWrapping = node.Props?.Multiline == true ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = node.Props?.Multiline == true ? 64 : 0,
            };
            if (placeholder is not null) AutomationProperties.SetHelpText(tb, placeholder);
            input = tb;
        }
        return WithField(node, input);
    }

    private FrameworkElement BuildSelect(SduiNode node)
    {
        var combo = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };
        foreach (var opt in node.Props?.Options ?? Array.Empty<SduiOption>())
        {
            var label = opt.LabelKey is { } k ? _l10n.Resolve(k, null, opt.Label ?? opt.Value)
                      : opt.Label ?? opt.Value;
            if (opt.LabelKey is not null) I18nResolved++;
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = opt.Value });
        }
        // Seleção inicial (DefaultValue casa Value da opção).
        if (node.Props?.DefaultValue is { } dv)
            combo.SelectedIndex = (node.Props?.Options ?? Array.Empty<SduiOption>())
                .ToList().FindIndex(o => o.Value == dv);
        return WithField(node, combo);
    }

    private FrameworkElement BuildCheckbox(SduiNode node)
    {
        var cb = new CheckBox
        {
            Content = ResolveText(node),
            IsChecked = node.Props?.Checked ?? false,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        return WithField(node, cb);
    }

    private FrameworkElement BuildSwitch(SduiNode node)
    {
        // WPF não tem toggle-switch nativo; ToggleButton estilizado como pílula.
        var tgl = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = ResolveText(node),
            IsChecked = node.Props?.Checked ?? false,
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return WithField(node, tgl);
    }

    private FrameworkElement BuildSlider(SduiNode node)
    {
        var sl = new Slider
        {
            Minimum = node.Props?.Min ?? 0,
            Maximum = node.Props?.Max ?? 1,
            Value = ParseValue(node, node.Props?.Min ?? 0),
            TickFrequency = node.Props?.Step ?? 0,
            IsSnapToTickEnabled = node.Props?.Step is > 0,
            MinWidth = 120,
        };
        return WithField(node, sl);
    }

    private FrameworkElement BuildStepper(SduiNode node)
    {
        double min = node.Props?.Min ?? 0, max = node.Props?.Max ?? double.MaxValue;
        double step = node.Props?.Step ?? 1;
        var value = ParseValue(node, min);
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 32, TextAlignment = TextAlignment.Center };
        label.Text = value.ToString(CultureInfo.InvariantCulture);
        var minus = new Button { Content = "−", Width = 28 };
        var plus = new Button { Content = "+", Width = 28 };
        void Refresh() => label.Text = value.ToString(CultureInfo.InvariantCulture);
        minus.Click += (_, _) => { value = Math.Max(min, value - step); Refresh(); };
        plus.Click += (_, _) => { value = Math.Min(max, value + step); Refresh(); };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(minus);
        panel.Children.Add(label);
        panel.Children.Add(plus);
        return WithField(node, panel);
    }

    private double ParseValue(SduiNode node, double fallback) =>
        double.TryParse(node.Props?.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : node.Props?.Value ?? fallback;

    private FrameworkElement BuildTabBar(SduiNode node)
    {
        var tabs = node.Tabs ?? Array.Empty<SduiTab>();
        var tc = new TabControl { TabStripPlacement = System.Windows.Controls.Dock.Bottom };
        var screens = (node.Children ?? Array.Empty<SduiNode>())
            .Where(c => c.Type == SduiNodeType.Screen)
            .ToDictionary(c => c.Nav?.Route ?? c.Id, c => c);
        foreach (var tab in tabs)
        {
            var header = tab.LabelKey is { } k ? _l10n.Resolve(k, null, tab.Label ?? tab.Route) : tab.Label ?? tab.Route;
            if (tab.LabelKey is not null) I18nResolved++;
            var content = screens.TryGetValue(tab.Route, out var scr) ? Build(scr) : new Grid();
            tc.Items.Add(new TabItem { Header = header, Content = content });
        }
        return tc;
    }

    private FrameworkElement BuildGrid(SduiNode node)
    {
        int cols = Math.Max(1, node.Props?.Columns ?? 2);
        var uni = new System.Windows.Controls.Primitives.UniformGrid { Columns = cols };
        double spacing = node.Props?.Spacing ?? 0;
        foreach (var child in node.Children ?? Array.Empty<SduiNode>())
        {
            var view = Build(child);
            if (spacing > 0) view.Margin = new Thickness(spacing / 2);
            uni.Children.Add(view);
        }
        return uni;
    }

    private FrameworkElement BuildSheet(SduiNode node)
    {
        // Painel modal: renderiza o conteúdo num cartão elevado; visível conforme
        // Props.Presented. Um host móvel usaria apresentação modal nativa.
        var content = BuildStack(node, Orientation.Vertical);
        var card = new Border
        {
            Child = content,
            Background = Brush(ResolveBackground(node.Props) ?? 0xFFFFFFFFu),
            CornerRadius = new CornerRadius(node.Props?.CornerRadius ?? 12),
            Padding = new Thickness(16),
            BorderBrush = Brush(0xE0E0E0FFu),
            BorderThickness = new Thickness(1),
            Visibility = node.Props?.Presented == true ? Visibility.Visible : Visibility.Collapsed,
        };
        return card;
    }

    private FrameworkElement BuildAvatar(SduiNode node)
    {
        double d = node.Props?.Width ?? node.Props?.Height ?? 40;
        var initials = new TextBlock
        {
            Text = ResolveText(node),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = d * 0.4,
            Foreground = Brush(ResolveColor(node.Props) ?? 0xFFFFFFFFu),
        };
        return new Border
        {
            Width = d, Height = d,
            CornerRadius = new CornerRadius(d / 2),
            Background = Brush(ResolveBackground(node.Props) ?? 0x8A8A8AFFu),
            Child = initials,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private FrameworkElement BuildChip(SduiNode node)
    {
        var tb = new TextBlock
        {
            Text = ResolveText(node),
            FontSize = node.Props?.FontSize ?? 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(ResolveColor(node.Props) ?? 0x333333FFu),
        };
        return new Border
        {
            Child = tb,
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(node.Props?.CornerRadius ?? 14),
            Background = Brush(ResolveBackground(node.Props) ?? 0xEDEDEDFFu),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private FrameworkElement BuildMediaPlaceholder(SduiNode node, string glyph)
    {
        // WSL/headless: sem player nativo. Renderiza um cartão com o glifo + src,
        // provando o mapeamento do nó (o host móvel real usa AVPlayer/MediaElement).
        var label = new TextBlock
        {
            Text = $"{glyph}  {node.Props?.Src ?? node.Media?.Poster ?? ""}".Trim(),
            Foreground = Brush(0xFFFFFFFFu),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };
        return new Border
        {
            Child = label,
            Background = Brush(0x202024FFu),
            CornerRadius = new CornerRadius(node.Props?.CornerRadius ?? 8),
            MinHeight = node.Props?.Height ?? 120,
        };
    }

    // ── List virtualizada ─────────────────────────────────────────────────────
    private FrameworkElement BuildVirtualizedList(SduiNode node, SduiListData data)
    {
        var items = data.Items ?? Array.Empty<SduiListItem>();
        LastListLogicalCount = data.Count ?? items.Count;

        var lb = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemsSource = items,
        };
        // Virtualização + reciclagem (o ponto do tipo List).
        VirtualizingPanel.SetIsVirtualizing(lb, true);
        VirtualizingPanel.SetVirtualizationMode(lb, VirtualizationMode.Recycling);
        bool horizontal = data.Axis == SduiAxis.Horizontal;
        ScrollViewer.SetHorizontalScrollBarVisibility(lb, horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(lb, horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);

        // Linha = ItemTemplate instanciado com binding aos dados da linha.
        var dt = new DataTemplate();
        var f = new FrameworkElementFactory(typeof(ContentControl));
        f.SetValue(ContentControl.FocusableProperty, false);
        f.SetValue(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        f.SetBinding(ContentControl.ContentProperty,
            new Binding(".") { Converter = new ListItemConverter(this, data.ItemTemplate) });
        dt.VisualTree = f;
        lb.ItemTemplate = dt;

        // Container enxuto; mantém o highlight nativo de seleção.
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6)));
        lb.ItemContainerStyle = itemStyle;

        LastList = lb;
        return lb;
    }

    /// Constrói a view de UMA linha: instancia o ItemTemplate com o contexto de
    /// binding da linha e o torna clicável (item.OnTap ?? template.OnTap).
    internal FrameworkElement BuildListRow(SduiNode template, SduiListItem item)
    {
        var prev = _row;
        _row = item.Data;
        try
        {
            var view = Build(StripOnTap(template)); // tap tratado no nível da linha
            var action = item.OnTap ?? template.OnTap;
            if (action is not null)
            {
                var rowNode = new SduiNode
                {
                    Id = item.Id,
                    Type = SduiNodeType.Card,
                    Props = new SduiProps { Data = item.Data },
                };
                view = MakeClickable(view, rowNode, action);
            }
            return view;
        }
        finally { _row = prev; }
    }

    /// Cópia do template sem OnTap (o tap é ligado por linha, não no template).
    private static SduiNode StripOnTap(SduiNode n) => new()
    {
        Id = n.Id, Type = n.Type, Props = n.Props, Children = n.Children,
        A11y = n.A11y, Fallback = n.Fallback, MinSchemaVersion = n.MinSchemaVersion,
        Responsive = n.Responsive, List = n.List, Nav = n.Nav, Bind = n.Bind,
        Animation = n.Animation, Media = n.Media, Validation = n.Validation,
        Tabs = n.Tabs, OnAppear = n.OnAppear, OnDisappear = n.OnDisappear,
        OnTap = null,
    };

    // ── NavStack ───────────────────────────────────────────────────────────────
    private FrameworkElement BuildNavStack(SduiNode node)
    {
        var screens = (node.Children ?? Array.Empty<SduiNode>())
            .Where(c => c.Type == SduiNodeType.Screen).ToList();
        Nav = new NavHost(this, screens);
        return Nav.View;
    }

    // ── Clique / navegação ──────────────────────────────────────────────────────
    private FrameworkElement MakeClickable(FrameworkElement inner, SduiNode node, SduiAction action)
    {
        var btn = new Button
        {
            Content = inner,
            Template = ChromelessTemplate(),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ToolTip = $"{action.Name} (id={node.Id})",
        };
        btn.Click += (_, _) =>
        {
            OnAction?.Invoke(node, action);
            if (action.Navigate is { } nav) Nav?.Apply(nav);
        };
        TapButtons.Add(btn);
        return btn;
    }

    private static ControlTemplate ChromelessTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "root");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6, "root"));
        template.Triggers.Add(pressed);
        return template;
    }

    // ── a11y / responsivo / bind ─────────────────────────────────────────────
    private void ApplyA11y(SduiNode node, FrameworkElement el)
    {
        if (node.A11y is not { } a) return;
        A11yApplied++;
        if (a.Label is { } l) AutomationProperties.SetName(el, l);
        if (a.Hint is { } h) AutomationProperties.SetHelpText(el, h);
        if (a.Value is { } v) AutomationProperties.SetItemStatus(el, v);
        if (a.Role == SduiA11yRole.Header) AutomationProperties.SetHeadingLevel(el, AutomationHeadingLevel.Level1);
        if (a.Hidden == true) AutomationProperties.SetIsOffscreenBehavior(el, IsOffscreenBehavior.Offscreen);
    }

    private double ApplyResponsiveFontSize(SduiNode node, double baseFs)
    {
        if (node.Responsive is not { Count: > 0 } overrides) return baseFs;
        foreach (var ov in overrides)
        {
            bool widthOk = ov.WidthClass is null or SduiSizeClass.Any || ov.WidthClass == SizeClass;
            bool bpOk = ov.MinContainerWidth is not { } min || _containerWidth >= min;
            if (widthOk && bpOk && ov.Props.FontSize is { } fs)
            {
                ResponsiveApplied++;
                return fs;
            }
        }
        return baseFs;
    }

    private static void ApplyConstraints(SduiProps? p, FrameworkElement el)
    {
        if (p is null) return;
        if (p.MinWidth is { } mw) el.MinWidth = mw;
        if (p.MaxWidth is { } xw) el.MaxWidth = xw;
        if (p.MinHeight is { } mh) el.MinHeight = mh;
        if (p.MaxHeight is { } xh) el.MaxHeight = xh;
    }

    private string ResolveText(SduiNode node)
    {
        // 1) binding de linha (List virtualizada) tem precedência.
        if (node.Bind is { } bind && bind.TryGetValue("text", out var key)
            && _row is { } row && row.TryGetValue(key, out var val))
            return val;
        // 2) i18n: TextKey → tabela do locale ativo (interpolação de TextArgs).
        if (node.Props?.TextKey is not null)
        {
            I18nResolved++;
            return _l10n.ResolveNode(node.Props) ?? "";
        }
        return node.Props?.Text ?? "";
    }

    // ── Box / size / helpers de estilo ─────────────────────────────────────────
    // Onda 🟡: cores resolvem token de tema → cru (ResolveBackground/BorderColor).
    private FrameworkElement Box(SduiProps? props, FrameworkElement child)
    {
        if (props is null) return child;
        var bgc = ResolveBackground(props);
        var bcc = ResolveBorderColor(props);
        bool hasBox = bgc is not null || props.BorderWidth is not null
                   || props.CornerRadius is not null || props.Padding is not null;
        if (!hasBox) return child;
        var border = new Border { Child = child };
        if (bgc is { } bg) border.Background = Brush(bg);
        if (props.CornerRadius is { } cr) border.CornerRadius = new CornerRadius(cr);
        if (props.BorderWidth is { } bw) border.BorderThickness = new Thickness(bw);
        if (bcc is { } bc) border.BorderBrush = Brush(bc);
        if (props.Padding is { } p) border.Padding = new Thickness(p.Left, p.Top, p.Right, p.Bottom);
        return border;
    }

    private static void ApplySize(SduiProps? props, FrameworkElement el)
    {
        if (props?.Width is { } w) el.Width = w;
        if (props?.Height is { } h) el.Height = h;
    }

    internal static Color ColorOf(uint rgba) => Color.FromArgb(
        (byte)(rgba & 0xFF), (byte)((rgba >> 24) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF));
    internal static SolidColorBrush Brush(uint rgba) => new(ColorOf(rgba));

    private static FontWeight Weight(SduiFontWeight? w) => w switch
    {
        SduiFontWeight.Medium => FontWeights.Medium,
        SduiFontWeight.Semibold => FontWeights.SemiBold,
        SduiFontWeight.Bold => FontWeights.Bold,
        _ => FontWeights.Normal,
    };

    private static HorizontalAlignment CrossH(SduiAlign a) => a switch
    {
        SduiAlign.Start => HorizontalAlignment.Left,
        SduiAlign.Center => HorizontalAlignment.Center,
        SduiAlign.End => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Stretch,
    };

    private static VerticalAlignment CrossV(SduiAlign a) => a switch
    {
        SduiAlign.Start => VerticalAlignment.Top,
        SduiAlign.Center => VerticalAlignment.Center,
        SduiAlign.End => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Stretch,
    };

    private static void AddLeftMargin(FrameworkElement el, double m)
        => el.Margin = new Thickness(el.Margin.Left + m, el.Margin.Top, el.Margin.Right, el.Margin.Bottom);
    private static void AddTopMargin(FrameworkElement el, double m)
        => el.Margin = new Thickness(el.Margin.Left, el.Margin.Top + m, el.Margin.Right, el.Margin.Bottom);

    // =========================================================================
    // NavHost: navegação nativa (pilha de Screens + nav bar + back).
    // =========================================================================
    public sealed class NavHost
    {
        private readonly MabelWindowsBuilder _b;
        private readonly Dictionary<string, SduiNode> _routes = new();
        private readonly List<SduiNode> _stack = new();
        private readonly ContentControl _body = new()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        private readonly TextBlock _title = new()
        {
            FontSize = 15, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(0x1A1A2EFF),
        };
        private readonly Button _back;

        public FrameworkElement View { get; }
        public int Depth => _stack.Count;
        public string? CurrentRoute => _stack.Count > 0 ? _stack[^1].Nav?.Route : null;
        public string? CurrentTitle => _title.Text;

        public NavHost(MabelWindowsBuilder b, List<SduiNode> screens)
        {
            _b = b;
            foreach (var s in screens)
                if (s.Nav?.Route is { } r) _routes[r] = s;

            _back = new Button
            {
                Content = "←",
                FontSize = 16, Width = 34, Margin = new Thickness(4, 0, 8, 0),
                Visibility = Visibility.Collapsed, Cursor = Cursors.Hand, ToolTip = "Voltar",
            };
            _back.Click += (_, _) => Apply(new SduiNavigate { Kind = SduiNavKind.Pop });

            var bar = new DockPanel { LastChildFill = true, Height = 44 };
            var barBg = new Border
            {
                Background = Brush(0xFFFFFFFFu),
                BorderBrush = Brush(0xEDF0F0FFu),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = bar,
            };
            DockPanel.SetDock(_back, Dock.Left);
            bar.Children.Add(_back);
            bar.Children.Add(_title);

            var root = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(barBg, Dock.Top);
            root.Children.Add(barBg);
            root.Children.Add(_body);
            View = root;

            if (screens.Count > 0) Push(screens[0]); // raiz
        }

        public void Apply(SduiNavigate n)
        {
            Console.WriteLine($"[navigate] kind={n.Kind} route={n.Route ?? "-"} depth={Depth}");
            switch (n.Kind)
            {
                case SduiNavKind.Push when n.Route is { } r && _routes.TryGetValue(r, out var s): Push(s); break;
                case SduiNavKind.Pop: Pop(); break;
                case SduiNavKind.Replace when n.Route is { } r && _routes.TryGetValue(r, out var s):
                    if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
                    Push(s); break;
                case SduiNavKind.Root:
                    if (n.Route is { } rr && _routes.TryGetValue(rr, out var rs)) { _stack.Clear(); Push(rs); }
                    else { while (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1); Render(); }
                    break;
                case SduiNavKind.PopTo when n.Route is { } r:
                    while (_stack.Count > 1 && _stack[^1].Nav?.Route != r) _stack.RemoveAt(_stack.Count - 1);
                    Render(); break;
            }
        }

        private void Push(SduiNode screen) { _stack.Add(screen); Render(); }
        private void Pop() { if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1); Render(); }

        private void Render()
        {
            var top = _stack[^1];
            _body.Content = _b.Build(top);
            _title.Text = top.Nav?.Title ?? "";
            _back.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Converte um SduiListItem na view WPF da linha (chamado por container realizado).
    private sealed class ListItemConverter : IValueConverter
    {
        private readonly MabelWindowsBuilder _b;
        private readonly SduiNode _template;
        public ListItemConverter(MabelWindowsBuilder b, SduiNode template) { _b = b; _template = template; }

        public object? Convert(object? value, Type t, object? p, CultureInfo c)
            => value is SduiListItem item ? _b.BuildListRow(_template, item) : null;
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
    }
}
