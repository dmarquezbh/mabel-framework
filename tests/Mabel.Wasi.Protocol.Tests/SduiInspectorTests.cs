using System.Text.Json;
using Mabel.Wasi.Protocol.DevTools;
using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// DevTools — inspector da árvore SDUI. Prova que o dump: (a) conta/tipa os nós,
/// (b) resolve tokens de tema e chaves i18n (claro vs escuro, pt vs en),
/// (c) marca o nó tipo-200 como não suportado com a política de fallback,
/// (d) emite texto navegável e JSON hierárquico.
/// </summary>
public class SduiInspectorTests
{
    [Fact]
    public void Describe_CountsNodes_AndFlagsUnsupported()
    {
        var tree = SduiInspector.Describe(Fixtures.Rich());

        // Screen + Text + TextField + Button + tipo-200 = 5 nós.
        Assert.Equal(5, tree.NodeCount);
        Assert.Equal(1, tree.UnsupportedCount);
        Assert.Equal(3, tree.SchemaVersion);
        Assert.Contains("unknown(200)", tree.CountsByType.Keys);

        var future = tree.Root.Children!.Single(n => n.Id == "future");
        Assert.False(future.Supported);
        Assert.Equal("Placeholder", future.Fallback);
        Assert.Equal(200, future.TypeCode);
    }

    [Fact]
    public void Describe_ResolvesI18n_ForActiveLocale()
    {
        var pt = SduiInspector.Describe(Fixtures.Rich(), new SduiInspectorOptions { Locale = "pt-BR" });
        var en = SduiInspector.Describe(Fixtures.Rich(), new SduiInspectorOptions { Locale = "en" });

        var ptTitle = pt.Root.Children!.Single(n => n.Id == "title");
        var enTitle = en.Root.Children!.Single(n => n.Id == "title");

        Assert.Equal("Olá, Daniel", ptTitle.Resolved!["text"]);
        Assert.Equal("Hello, Daniel", enTitle.Resolved!["text"]);
    }

    [Fact]
    public void Describe_ResolvesThemeTokens_LightVsDark()
    {
        var light = SduiInspector.Describe(Fixtures.Rich(), new SduiInspectorOptions { ThemeMode = SduiThemeMode.Light });
        var dark = SduiInspector.Describe(Fixtures.Rich(), new SduiInspectorOptions { ThemeMode = SduiThemeMode.Dark });

        // Screen.background = token "surface" → cor diferente por tema.
        Assert.Equal("#FFFFFFFF", light.Root.Resolved!["background"]);
        Assert.Equal("#1A1A2EFF", dark.Root.Resolved!["background"]);
        Assert.True(dark.Dark);
        Assert.False(light.Dark);
    }

    [Fact]
    public void Describe_IncludesInputState()
    {
        var tree = SduiInspector.Describe(Fixtures.Rich(), new SduiInspectorOptions { Locale = "pt-BR" });
        var field = tree.Root.Children!.Single(n => n.Id == "name");

        // Placeholder localizado + estado inicial vazio.
        Assert.Equal("Nome", field.Resolved!["placeholder"]);
        Assert.Equal("", field.Resolved!["state.value"]);
        Assert.Equal("name", field.Props!["field"]);
    }

    [Fact]
    public void ToText_ProducesNavigableTree()
    {
        var text = SduiInspector.ToText(Fixtures.Rich(), new SduiInspectorOptions { Locale = "pt-BR" });

        Assert.Contains("Screen #root", text);
        Assert.Contains("├─ ", text);
        Assert.Contains("└─ ", text);
        Assert.Contains("[fallback=Placeholder]", text);
        Assert.Contains("onTap=submit", text);
    }

    [Fact]
    public void ToJson_IsHierarchical_AndParses()
    {
        var json = SduiInspector.ToJson(Fixtures.Rich(), new SduiInspectorOptions { Locale = "en" });
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement.GetProperty("root");
        Assert.Equal("Screen", root.GetProperty("type").GetString());
        Assert.Equal(4, root.GetProperty("children").GetArrayLength());
    }
}
