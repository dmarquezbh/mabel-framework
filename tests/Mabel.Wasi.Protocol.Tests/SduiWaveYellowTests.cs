using System.Text.Json;
using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Onda 🟡 — catálogo ampliado, media, animações, tabs e lifecycle. Round-trip
/// dos novos tipos de nó + PROVA de retrocompat: os campos v3 são omitidos quando
/// ausentes (um host v2 recebe exatamente o mesmo JSON de antes), e os novos
/// tipos de nó continuam reconhecidos/desconhecidos pela regra de fallback.
/// </summary>
public class SduiWaveYellowTests
{
    private static void AssertStable(SduiDocument doc)
    {
        var first = SduiJson.Serialize(doc);
        var second = SduiJson.Serialize(SduiJson.Deserialize(first)!);
        Assert.Equal(first, second);
    }

    [Fact]
    public void NewNodeTypes_AreKnownInThisSchema()
    {
        foreach (var t in new[]
        {
            SduiNodeType.TextField, SduiNodeType.Select, SduiNodeType.Checkbox, SduiNodeType.Switch,
            SduiNodeType.Slider, SduiNodeType.Stepper, SduiNodeType.TabBar, SduiNodeType.Grid,
            SduiNodeType.Sheet, SduiNodeType.Avatar, SduiNodeType.Chip, SduiNodeType.Video, SduiNodeType.Audio,
        })
            Assert.True(t.IsKnown(), $"{t} deveria ser conhecido no schema v{SduiSchema.CurrentVersion}");

        Assert.Equal(3, SduiSchema.CurrentVersion);
    }

    [Fact]
    public void Catalog_TabBarGridChipAvatar_RoundTrips()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "tabs", Type = SduiNodeType.TabBar,
                Tabs =
                [
                    new SduiTab { Route = "home", Label = "Início", Icon = "house", Badge = "3" },
                    new SduiTab { Route = "profile", LabelKey = "tab.profile", Icon = "person" },
                ],
                Children =
                [
                    new SduiNode
                    {
                        Id = "s:home", Type = SduiNodeType.Screen, Nav = new SduiNav { Route = "home" },
                        Children =
                        [
                            new SduiNode
                            {
                                Id = "grid", Type = SduiNodeType.Grid, Props = new SduiProps { Columns = 3, Spacing = 8 },
                                Children =
                                [
                                    new SduiNode { Id = "av", Type = SduiNodeType.Avatar, Props = new SduiProps { Text = "DM", Width = 48 } },
                                    new SduiNode { Id = "chip", Type = SduiNodeType.Chip, Props = new SduiProps { Text = "Novo", Background = 0xEDEDEDFFu } },
                                ],
                            },
                        ],
                    },
                ],
            },
        };
        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal(2, back.Root.Tabs!.Count);
        Assert.Equal("3", back.Root.Tabs![0].Badge);
        Assert.Equal(3, back.Root.Children![0].Children![0].Props!.Columns);
    }

    [Fact]
    public void Media_VideoWithPlaybackMetadata_RoundTrips()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "v", Type = SduiNodeType.Video,
                Props = new SduiProps { Src = "intro.mp4" },
                Media = new SduiMedia { Autoplay = true, Loop = false, Muted = true, Controls = true, Poster = "intro.jpg", Fit = SduiContentFit.Cover },
            },
        };
        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.True(back.Root.Media!.Autoplay);
        Assert.Equal(SduiContentFit.Cover, back.Root.Media!.Fit);
        Assert.Equal("intro.jpg", back.Root.Media!.Poster);
    }

    [Fact]
    public void Animation_And_NavTransition_RoundTrip()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "card", Type = SduiNodeType.Card,
                Animation = new SduiAnimation
                {
                    Kind = SduiAnimationKind.Slide,
                    Trigger = SduiAnimationTrigger.OnAppear,
                    DurationMs = 300, DelayMs = 50,
                    Easing = SduiEasing.Spring, Direction = SduiSlideFrom.Bottom,
                },
                OnTap = new SduiAction("go")
                {
                    Navigate = new SduiNavigate { Kind = SduiNavKind.Push, Route = "next", Transition = SduiNavTransition.Fade },
                },
            },
        };
        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal(SduiAnimationKind.Slide, back.Root.Animation!.Kind);
        Assert.Equal(SduiEasing.Spring, back.Root.Animation!.Easing);
        Assert.Equal(SduiNavTransition.Fade, back.Root.OnTap!.Navigate!.Transition);
    }

    [Fact]
    public void Lifecycle_OnAppearOnDisappear_RoundTrip()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "screen", Type = SduiNodeType.Screen,
                OnAppear = new SduiAction("track", new Dictionary<string, string> { ["ev"] = "view" }),
                OnDisappear = new SduiAction("track", new Dictionary<string, string> { ["ev"] = "leave" }),
            },
        };
        AssertStable(doc);

        var back = SduiJson.Deserialize(SduiJson.Serialize(doc))!;
        Assert.Equal("track", back.Root.OnAppear!.Name);
        Assert.Equal("view", back.Root.OnAppear!.Args!["ev"]);
        Assert.Equal("leave", back.Root.OnDisappear!.Args!["ev"]);
    }

    // ── Retrocompat ─────────────────────────────────────────────────────────

    [Fact]
    public void V2Document_EmitsNoV3Keys_ByteForByteCompat()
    {
        // Um documento "clássico" (sem recursos v3) NÃO deve ganhar chaves novas —
        // os campos v3 são todos opcionais e omitidos quando null. Isto garante que
        // um host v2 recebe exatamente o mesmo JSON.
        var doc = new SduiDocument
        {
            SchemaVersion = 2,
            Root = new SduiNode
            {
                Id = "card:1", Type = SduiNodeType.Card,
                Props = new SduiProps { CornerRadius = 8, Background = 0xFFFFFFFFu },
                Children = [new SduiNode { Id = "t", Type = SduiNodeType.Text, Props = new SduiProps { Text = "Olá" } }],
            },
        };
        var json = SduiJson.Serialize(doc);

        foreach (var v3Key in new[]
        {
            "themes", "themeMode", "localization", "animation", "media", "validation",
            "tabs", "onAppear", "onDisappear", "backgroundToken", "colorToken", "textStyle",
            "textKey", "field", "options", "columns", "presented",
        })
            Assert.False(json.Contains($"\"{v3Key}\""), $"JSON v2 não deveria conter a chave v3 '{v3Key}': {json}");
    }

    [Fact]
    public void FutureUnknownType_StillFallsBack_WithV3NodesPresent()
    {
        // Tipo 200 continua desconhecido mesmo com o schema ampliado; um input v3
        // conhecido convive na mesma árvore sem quebrar o parse.
        const string json = """
        {
          "schemaVersion": 99,
          "root": {
            "id": "root", "type": 2,
            "children": [
              { "id": "future", "type": 200, "fallback": 1 },
              { "id": "field", "type": 15, "props": { "field": "nome", "placeholder": "Nome" } }
            ]
          }
        }
        """;
        var doc = SduiJson.Deserialize(json)!;
        var future = doc.Root.Children![0];
        var field = doc.Root.Children![1];

        Assert.False(future.Type.IsKnown());
        Assert.Equal(SduiUnknownFallback.Placeholder, future.ResolveFallback(SduiSchema.CurrentVersion));
        Assert.Equal(SduiNodeType.TextField, field.Type);
        Assert.True(field.Type.IsKnown());
        Assert.Equal("nome", field.Props!.Field);
    }

    [Fact]
    public void EnumsSerializeAsNumbers_ForNewTypes()
    {
        var doc = new SduiDocument
        {
            Root = new SduiNode { Id = "n", Type = SduiNodeType.Chip }, // 0x19 = 25
        };
        var json = SduiJson.Serialize(doc);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(25, parsed.RootElement.GetProperty("root").GetProperty("type").GetInt32());
    }
}
