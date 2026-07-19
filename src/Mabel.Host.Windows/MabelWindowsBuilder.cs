using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Host.Windows;

// =============================================================================
// Mabel SDUI - host Windows (WPF)
// Decodifica um SduiDocument (a MESMA arvore semantica emitida pelo guest e
// consumida pelo host iOS) e instancia CONTROLES NATIVOS WPF reais
// (ScrollViewer / Grid / Border / TextBlock / ProgressBar / Button). Sem canvas,
// sem pixels: scroll, hit-testing, foco de teclado e acessibilidade (UI
// Automation) vem do SO. Espelha Mabel.Wasi.Protocol/Sdui/Descriptor.cs e o
// mapeamento do MabelSdui.swift (iOS).
//
// Layout: stacks viram Grid (1 linha/coluna por filho). flex>0 => estrela (*)
// no eixo (cresce); senao Auto (tamanho do conteudo) - equivale ao content
// hugging do UIStackView iOS. align mapeia pro alinhamento no eixo cruzado.
// =============================================================================
public sealed class MabelWindowsBuilder
{
    /// Callback disparado quando um no clicavel (onTap) e acionado.
    public Action<SduiNode, SduiAction>? OnAction { get; set; }

    /// Estatisticas de render (nos instanciados por tipo) - evidencia do spike.
    public readonly Dictionary<SduiNodeType, int> Counts = new();
    /// Botoes nativos gerados p/ nos com onTap (usado pelo --selftest).
    public readonly List<Button> TapButtons = new();
    public int NodeCount { get; private set; }

    public FrameworkElement Build(SduiDocument doc) => Build(doc.Root);

    private FrameworkElement Build(SduiNode node)
    {
        NodeCount++;
        Counts[node.Type] = Counts.TryGetValue(node.Type, out var c) ? c + 1 : 1;

        FrameworkElement element = node.Type switch
        {
            SduiNodeType.Screen      => Box(node.Props, BuildFirstChild(node)),
            SduiNodeType.ScrollView  => BuildScroll(node),
            SduiNodeType.VStack      => Box(node.Props, BuildStack(node, Orientation.Vertical)),
            SduiNodeType.HStack      => Box(node.Props, BuildStack(node, Orientation.Horizontal)),
            SduiNodeType.List        => Box(node.Props, BuildStack(node,
                                            node.Props?.Axis == SduiAxis.Horizontal
                                                ? Orientation.Horizontal : Orientation.Vertical)),
            SduiNodeType.Card        => BuildCard(node),
            SduiNodeType.Text        => BuildText(node),
            SduiNodeType.Button      => BuildText(node),   // proof: botao -> label estilizado
            SduiNodeType.Badge       => BuildBadge(node),
            SduiNodeType.ProgressBar => BuildProgress(node),
            SduiNodeType.Divider     => BuildDivider(node),
            SduiNodeType.Image       => new Border(),
            SduiNodeType.Spacer      => new Grid(),
            _                        => new Grid(),
        };

        ApplySize(node.Props, element);

        // onTap => torna o no clicavel com um Button nativo chromeless.
        if (node.OnTap is { } action)
            element = MakeClickable(element, node, action);

        return element;
    }

    private FrameworkElement BuildFirstChild(SduiNode node)
    {
        var child = node.Children?.FirstOrDefault();
        return child is null ? new Grid() : Build(child);
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
        if (node.Props?.Background is { } bg)
            sv.Background = Brush(bg);
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
            bool flex = (childNode.Props?.Flex ?? 0) > 0;

            if (orientation == Orientation.Horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = flex ? new GridLength(childNode.Props!.Flex!.Value, GridUnitType.Star)
                                 : GridLength.Auto,
                });
                Grid.SetColumn(child, i);
                child.VerticalAlignment = CrossV(align);
                if (i > 0) AddLeftMargin(child, spacing);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = flex ? new GridLength(childNode.Props!.Flex!.Value, GridUnitType.Star)
                                  : GridLength.Auto,
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
    {
        // Conteudo = stack vertical dos filhos (rows), com o spacing do card.
        var content = BuildStack(node, Orientation.Vertical);
        // Box (fundo/borda/canto/padding) do card.
        var boxed = Box(node.Props, content);
        return boxed;
    }

    private static TextBlock BuildText(SduiNode node)
    {
        var tb = new TextBlock
        {
            Text = node.Props?.Text ?? "",
            FontSize = node.Props?.FontSize ?? 14,
            FontWeight = Weight(node.Props?.Weight),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (node.Props?.Color is { } col) tb.Foreground = Brush(col);
        return tb;
    }

    private static Border BuildBadge(SduiNode node)
    {
        var tb = new TextBlock
        {
            Text = node.Props?.Text ?? "",
            FontSize = node.Props?.FontSize ?? 10,
            FontWeight = Weight(node.Props?.Weight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (node.Props?.Color is { } col) tb.Foreground = Brush(col);

        // Pilula: padding interno fixo (espelha o PaddingLabel do iOS: 2/6).
        var pill = new Border
        {
            Child = tb,
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (node.Props?.Background is { } bg) pill.Background = Brush(bg);
        if (node.Props?.CornerRadius is { } cr) pill.CornerRadius = new CornerRadius(cr);
        return pill;
    }

    private static ProgressBar BuildProgress(SduiNode node)
    {
        var pb = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = node.Props?.Value ?? 0,
            Height = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            BorderThickness = new Thickness(0),
        };
        if (node.Props?.Color is { } col) pb.Foreground = Brush(col);
        return pb;
    }

    private static Border BuildDivider(SduiNode node)
    {
        var b = new Border { Height = 1 };
        b.Background = node.Props?.Background is { } bg
            ? Brush(bg) : new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        return b;
    }

    // ── Box / size / clique ────────────────────────────────────────────────

    /// Aplica fundo/borda/canto/padding se presentes; senao devolve o filho cru.
    private static FrameworkElement Box(SduiProps? props, FrameworkElement child)
    {
        if (props is null) return child;
        bool hasBox = props.Background is not null || props.BorderWidth is not null
                   || props.CornerRadius is not null || props.Padding is not null;
        if (!hasBox) return child;

        var border = new Border { Child = child };
        if (props.Background is { } bg) border.Background = Brush(bg);
        if (props.CornerRadius is { } cr) border.CornerRadius = new CornerRadius(cr);
        if (props.BorderWidth is { } bw) border.BorderThickness = new Thickness(bw);
        if (props.BorderColor is { } bc) border.BorderBrush = Brush(bc);
        if (props.Padding is { } p) border.Padding = new Thickness(p.Left, p.Top, p.Right, p.Bottom);
        return border;
    }

    private static void ApplySize(SduiProps? props, FrameworkElement el)
    {
        if (props?.Width is { } w) el.Width = w;
        if (props?.Height is { } h) el.Height = h;
    }

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
        btn.Click += (_, _) => OnAction?.Invoke(node, action);
        TapButtons.Add(btn);
        return btn;
    }

    /// Template de Button sem chrome: so um Border transparente (hit-testavel)
    /// com o conteudo. Feedback de press via opacidade.
    private static ControlTemplate ChromelessTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "root");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(cp);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6, "root"));
        template.Triggers.Add(pressed);
        return template;
    }

    // ── Helpers de estilo ──────────────────────────────────────────────────

    /// RGBA 0xRRGGBBAA -> WPF Color (mesmo formato do RenderCommand / iOS).
    private static Color ColorOf(uint rgba) => Color.FromArgb(
        (byte)(rgba & 0xFF),          // A
        (byte)((rgba >> 24) & 0xFF),  // R
        (byte)((rgba >> 16) & 0xFF),  // G
        (byte)((rgba >> 8) & 0xFF));  // B

    private static SolidColorBrush Brush(uint rgba) => new(ColorOf(rgba));

    private static FontWeight Weight(SduiFontWeight? w) => w switch
    {
        SduiFontWeight.Medium   => FontWeights.Medium,
        SduiFontWeight.Semibold => FontWeights.SemiBold,
        SduiFontWeight.Bold     => FontWeights.Bold,
        _                       => FontWeights.Normal,
    };

    private static HorizontalAlignment CrossH(SduiAlign a) => a switch
    {
        SduiAlign.Start  => HorizontalAlignment.Left,
        SduiAlign.Center => HorizontalAlignment.Center,
        SduiAlign.End    => HorizontalAlignment.Right,
        _                => HorizontalAlignment.Stretch,
    };

    private static VerticalAlignment CrossV(SduiAlign a) => a switch
    {
        SduiAlign.Start  => VerticalAlignment.Top,
        SduiAlign.Center => VerticalAlignment.Center,
        SduiAlign.End    => VerticalAlignment.Bottom,
        _                => VerticalAlignment.Stretch,
    };

    private static void AddLeftMargin(FrameworkElement el, double m)
        => el.Margin = new Thickness(el.Margin.Left + m, el.Margin.Top, el.Margin.Right, el.Margin.Bottom);

    private static void AddTopMargin(FrameworkElement el, double m)
        => el.Margin = new Thickness(el.Margin.Left, el.Margin.Top + m, el.Margin.Right, el.Margin.Bottom);
}
