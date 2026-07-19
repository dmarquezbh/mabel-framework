using System.Text.Json;
using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// ATDD round-trip: cada feature fundacional da Onda 1 deve deserializar →
/// percorrer → re-serializar sem perda. A prova é a ESTABILIDADE do JSON
/// canônico: Serialize(Deserialize(Serialize(x))) == Serialize(x).
/// </summary>
public class SduiRoundTripTests
{
    /// <summary>Serializa, deserializa e re-serializa; devolve os dois JSONs.</summary>
    private static (string first, string second) RoundTrip(SduiDocument doc)
    {
        var first = SduiJson.Serialize(doc);
        var back = SduiJson.Deserialize(first);
        Assert.NotNull(back);
        var second = SduiJson.Serialize(back!);
        return (first, second);
    }

    private static void AssertStable(SduiDocument doc)
    {
        var (first, second) = RoundTrip(doc);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Baseline_V1Document_RoundTripsStable()
    {
        var doc = new SduiDocument
        {
            Root = new SduiNode
            {
                Id = "screen:board",
                Type = SduiNodeType.Screen,
                Props = new SduiProps { Padding = SduiEdges.All(16), Background = 0xFFFFFFFFu },
                Children =
                [
                    new SduiNode
                    {
                        Id = "card:1",
                        Type = SduiNodeType.Card,
                        Props = new SduiProps { CornerRadius = 8, Spacing = 4 },
                        OnTap = new SduiAction("open", new Dictionary<string, string> { ["id"] = "1" }),
                        Children = [new SduiNode { Id = "t:1", Type = SduiNodeType.Text, Props = new SduiProps { Text = "Olá" } }],
                    },
                ],
            },
        };

        AssertStable(doc);
    }

    [Fact]
    public void Accessibility_RoundTripsAndPreservesSemantics()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "img:logo",
                Type = SduiNodeType.Image,
                Props = new SduiProps { Src = "logo" },
                A11y = new SduiA11y
                {
                    Label = "Logotipo Pjus",
                    Role = SduiA11yRole.Image,
                    Hint = "Toque duas vezes para ir ao início",
                    Hidden = false,
                    Value = "72%",
                    Traits = SduiA11yTraits.Selected | SduiA11yTraits.UpdatesFrequently,
                },
            },
        };

        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal("Logotipo Pjus", back.Root.A11y!.Label);
        Assert.Equal(SduiA11yRole.Image, back.Root.A11y!.Role);
        Assert.True(back.Root.A11y!.Traits!.Value.HasFlag(SduiA11yTraits.UpdatesFrequently));
    }

    [Fact]
    public void ResponsiveLayout_RoundTripsWithBreakpointsAndFlex()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "screen:adaptive",
                Type = SduiNodeType.Screen,
                Props = new SduiProps
                {
                    SafeArea = SduiSafeArea.Top | SduiSafeArea.Bottom,
                    MinWidth = 320,
                    MaxWidth = 1024,
                    AspectRatio = 16f / 9f,
                    FlexGrow = 1,
                    FlexShrink = 0,
                    FlexBasis = 240,
                    Wrap = SduiWrap.Wrap,
                },
                Responsive =
                [
                    new SduiResponsiveOverride
                    {
                        WidthClass = SduiSizeClass.Regular,
                        MinContainerWidth = 700,
                        Props = new SduiProps { Axis = SduiAxis.Horizontal, Spacing = 24 },
                    },
                    new SduiResponsiveOverride
                    {
                        WidthClass = SduiSizeClass.Compact,
                        Props = new SduiProps { Axis = SduiAxis.Vertical, Spacing = 8 },
                    },
                ],
            },
        };

        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal(2, back.Root.Responsive!.Count);
        Assert.Equal(SduiSizeClass.Regular, back.Root.Responsive![0].WidthClass);
        Assert.Equal(SduiSafeArea.Top | SduiSafeArea.Bottom, back.Root.Props!.SafeArea);
    }

    [Fact]
    public void VirtualizedList_RoundTripsWithTemplateAndDataWindow()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "list:cards",
                Type = SduiNodeType.List,
                List = new SduiListData
                {
                    Virtualized = true,
                    Axis = SduiAxis.Vertical,
                    EstimatedItemExtent = 88,
                    Count = 5000,
                    WindowStart = 0,
                    ItemTemplate = new SduiNode
                    {
                        Id = "tmpl:row",
                        Type = SduiNodeType.Card,
                        Bind = new Dictionary<string, string> { ["text"] = "titulo", ["value"] = "progresso" },
                        Children = [new SduiNode { Id = "tmpl:title", Type = SduiNodeType.Text }],
                    },
                    Items =
                    [
                        new SduiListItem { Id = "card:1", Data = new Dictionary<string, string> { ["titulo"] = "Credor A", ["progresso"] = "0.4" } },
                        new SduiListItem { Id = "card:2", Data = new Dictionary<string, string> { ["titulo"] = "Credor B", ["progresso"] = "0.9" }, OnTap = new SduiAction("open") },
                    ],
                },
            },
        };

        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.True(back.Root.List!.Virtualized);
        Assert.Equal(5000, back.Root.List!.Count);
        Assert.Equal(2, back.Root.List!.Items!.Count);
        Assert.Equal("titulo", back.Root.List!.ItemTemplate.Bind!["text"]);
    }

    [Fact]
    public void Navigation_RoundTripsStackScreensAndActions()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "nav:root",
                Type = SduiNodeType.NavStack,
                Children =
                [
                    new SduiNode
                    {
                        Id = "screen:board",
                        Type = SduiNodeType.Screen,
                        Nav = new SduiNav { Route = "board", Title = "Board", HidesNavBar = false },
                        Children =
                        [
                            new SduiNode
                            {
                                Id = "btn:open",
                                Type = SduiNodeType.Button,
                                Props = new SduiProps { Text = "Abrir card" },
                                OnTap = new SduiAction("navigate")
                                {
                                    Navigate = new SduiNavigate
                                    {
                                        Kind = SduiNavKind.Push,
                                        Route = "card/:id",
                                        Params = new Dictionary<string, string> { ["id"] = "50231" },
                                    },
                                },
                            },
                        ],
                    },
                    new SduiNode
                    {
                        Id = "screen:card",
                        Type = SduiNodeType.Screen,
                        Nav = new SduiNav { Route = "card/:id", Title = "Detalhe", Modal = true },
                    },
                ],
            },
        };

        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal(SduiNodeType.NavStack, back.Root.Type);
        var btn = back.Root.Children![0].Children![0];
        Assert.Equal(SduiNavKind.Push, btn.OnTap!.Navigate!.Kind);
        Assert.Equal("50231", btn.OnTap!.Navigate!.Params!["id"]);
        Assert.True(back.Root.Children![1].Nav!.Modal);
    }

    [Fact]
    public void CanonicalJson_IsCamelCase_AndEnumsAsNumbers()
    {
        var doc = new SduiDocument
        {
            Root = new SduiNode
            {
                Id = "n",
                Type = SduiNodeType.Button, // 0x08 = 8
                Props = new SduiProps { FontSize = 14 },
            },
        };

        var json = SduiJson.Serialize(doc);
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        // camelCase keys (bate com o decode Swift).
        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("root", out var rootNode));
        Assert.True(rootNode.TryGetProperty("props", out var props));
        Assert.True(props.TryGetProperty("fontSize", out _));

        // enum como número (host Swift decodifica UInt8), não string.
        Assert.Equal(JsonValueKind.Number, rootNode.GetProperty("type").ValueKind);
        Assert.Equal(8, rootNode.GetProperty("type").GetInt32());
    }
}
