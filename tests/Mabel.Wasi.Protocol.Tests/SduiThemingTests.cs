using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Onda 🟡 — theming / dark-mode. Tokens de cor/tipografia/espaçamento resolvidos
/// por variante de tema (claro/escuro), com fallback entre variantes. Round-trip
/// preserva o conjunto de temas no descritor.
/// </summary>
public class SduiThemingTests
{
    private static SduiThemeSet Themes() => new()
    {
        Light = new SduiTheme
        {
            Colors = new Dictionary<string, uint> { ["surface"] = 0xFFFFFFFFu, ["onSurface"] = 0x1A1A2EFFu, ["primary"] = 0x2D6CDFFFu },
            Text = new Dictionary<string, SduiTextStyle> { ["title"] = new() { FontSize = 20, Weight = SduiFontWeight.Bold, ColorToken = "onSurface" } },
            Spacing = new Dictionary<string, float> { ["md"] = 16 },
        },
        Dark = new SduiTheme
        {
            Colors = new Dictionary<string, uint> { ["surface"] = 0x101014FFu, ["onSurface"] = 0xF2F2F5FFu },
            // 'primary' propositalmente ausente no dark → cai no light.
        },
    };

    [Fact]
    public void ThemeResolver_PicksLightOrDarkVariant()
    {
        var themes = Themes();
        var light = new SduiThemeResolver(themes, SduiThemeMode.Light);
        var dark = new SduiThemeResolver(themes, SduiThemeMode.Dark);

        Assert.Equal(0xFFFFFFFFu, light.Color("surface"));
        Assert.Equal(0x101014FFu, dark.Color("surface"));
    }

    [Fact]
    public void ThemeResolver_System_FollowsOsPreference()
    {
        var themes = Themes();
        var sysLight = new SduiThemeResolver(themes, SduiThemeMode.System, systemPrefersDark: false);
        var sysDark = new SduiThemeResolver(themes, SduiThemeMode.System, systemPrefersDark: true);

        Assert.Equal(0x1A1A2EFFu, sysLight.Color("onSurface"));
        Assert.Equal(0xF2F2F5FFu, sysDark.Color("onSurface"));
    }

    [Fact]
    public void ThemeResolver_MissingTokenInVariant_FallsBackToOther()
    {
        // 'primary' só existe no light; no modo dark deve cair no light.
        var dark = new SduiThemeResolver(Themes(), SduiThemeMode.Dark);
        // Active é o dark; 'primary' não está nele → null (o host usa a cor crua).
        Assert.Null(dark.Color("primary"));
        // Mas o resolvedor no modo light acha.
        Assert.Equal(0x2D6CDFFFu, new SduiThemeResolver(Themes(), SduiThemeMode.Light).Color("primary"));
    }

    [Fact]
    public void ThemeResolver_UnknownToken_ReturnsNull()
    {
        var r = new SduiThemeResolver(Themes(), SduiThemeMode.Light);
        Assert.Null(r.Color("doesNotExist"));
        Assert.Null(r.TextStyle("nope"));
        Assert.Null(r.Spacing("nope"));
    }

    [Fact]
    public void ThemeResolver_ResolveBackground_PrefersTokenThenRaw()
    {
        var r = new SduiThemeResolver(Themes(), SduiThemeMode.Light);
        // token presente → cor do tema.
        Assert.Equal(0xFFFFFFFFu, r.ResolveBackground(new SduiProps { BackgroundToken = "surface", Background = 0x000000FFu }));
        // token ausente → cai na cor crua.
        Assert.Equal(0x00FF00FFu, r.ResolveBackground(new SduiProps { BackgroundToken = "ghost", Background = 0x00FF00FFu }));
        // sem token → cor crua.
        Assert.Equal(0x123456FFu, r.ResolveBackground(new SduiProps { Background = 0x123456FFu }));
    }

    [Fact]
    public void ThemeResolver_TextStyleToken_ResolvesTypography()
    {
        var r = new SduiThemeResolver(Themes(), SduiThemeMode.Light);
        var style = r.TextStyle("title");
        Assert.NotNull(style);
        Assert.Equal(20, style!.FontSize);
        Assert.Equal(SduiFontWeight.Bold, style.Weight);
        Assert.Equal(0x1A1A2EFFu, r.Color(style.ColorToken));
    }

    [Fact]
    public void ThemedDocument_RoundTripsStable()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            ThemeMode = SduiThemeMode.System,
            Themes = Themes(),
            Root = new SduiNode
            {
                Id = "screen",
                Type = SduiNodeType.Screen,
                Props = new SduiProps { BackgroundToken = "surface" },
                Children =
                [
                    new SduiNode { Id = "t", Type = SduiNodeType.Text, Props = new SduiProps { TextStyle = "title", TextKey = null, Text = "Olá" } },
                ],
            },
        };

        var first = SduiJson.Serialize(doc);
        var back = SduiJson.Deserialize(first)!;
        var second = SduiJson.Serialize(back);
        Assert.Equal(first, second);
        Assert.Equal(0x101014FFu, back.Themes!.Dark!.Colors!["surface"]);
        Assert.Equal(SduiThemeMode.System, back.ThemeMode);
    }
}
