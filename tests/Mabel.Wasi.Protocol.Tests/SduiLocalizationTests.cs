using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Onda 🟡 — i18n / l10n. Chaves resolvidas por locale com fallback em cadeia
/// (exato → base → default → texto cru → chave), interpolação de {args} e
/// pluralização simples. Round-trip preserva a tabela de strings.
/// </summary>
public class SduiLocalizationTests
{
    private static SduiLocalization L10n() => new()
    {
        DefaultLocale = "pt-BR",
        Locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["pt-BR"] = new Dictionary<string, string>
            {
                ["greeting"] = "Olá, {name}!",
                ["items.one"] = "{count} item",
                ["items.other"] = "{count} itens",
                ["save"] = "Salvar",
            },
            ["en"] = new Dictionary<string, string>
            {
                ["greeting"] = "Hello, {name}!",
                ["save"] = "Save",
            },
        },
    };

    [Fact]
    public void Localizer_ResolvesKey_ForActiveLocale()
    {
        var pt = new SduiLocalizer(L10n(), "pt-BR");
        var en = new SduiLocalizer(L10n(), "en");
        Assert.Equal("Salvar", pt.Resolve("save"));
        Assert.Equal("Save", en.Resolve("save"));
    }

    [Fact]
    public void Localizer_Interpolates_Args()
    {
        var pt = new SduiLocalizer(L10n(), "pt-BR");
        Assert.Equal("Olá, Daniel!", pt.Resolve("greeting", new Dictionary<string, string> { ["name"] = "Daniel" }));
    }

    [Fact]
    public void Localizer_BaseLocaleFallback()
    {
        // pt-PT não existe → cai em pt (base). Como não há 'pt', cai no default pt-BR.
        var ptPt = new SduiLocalizer(L10n(), "pt-PT");
        Assert.Equal("Salvar", ptPt.Resolve("save"));
    }

    [Fact]
    public void Localizer_MissingKey_FallsBackToRawTextThenKey()
    {
        var en = new SduiLocalizer(L10n(), "en");
        // 'items.one' não existe no en nem no default via pluralização → usa fallbackText.
        Assert.Equal("cru", en.Resolve("inexistente", null, "cru"));
        // sem fallbackText → a própria chave.
        Assert.Equal("inexistente", en.Resolve("inexistente"));
    }

    [Fact]
    public void Localizer_Pluralization_OneVsOther()
    {
        var pt = new SduiLocalizer(L10n(), "pt-BR");
        Assert.Equal("1 item", pt.Resolve("items", new Dictionary<string, string> { ["count"] = "1" }));
        Assert.Equal("5 itens", pt.Resolve("items", new Dictionary<string, string> { ["count"] = "5" }));
    }

    [Fact]
    public void Localizer_ResolveNode_UsesTextKeyOrRawText()
    {
        var pt = new SduiLocalizer(L10n(), "pt-BR");
        Assert.Equal("Salvar", pt.ResolveNode(new SduiProps { TextKey = "save", Text = "ignored" }));
        Assert.Equal("literal", pt.ResolveNode(new SduiProps { Text = "literal" }));
    }

    [Fact]
    public void LocalizedDocument_RoundTripsStable()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Localization = L10n(),
            Root = new SduiNode
            {
                Id = "t",
                Type = SduiNodeType.Text,
                Props = new SduiProps { TextKey = "greeting", TextArgs = new Dictionary<string, string> { ["name"] = "Ana" } },
            },
        };
        var first = SduiJson.Serialize(doc);
        var back = SduiJson.Deserialize(first)!;
        var second = SduiJson.Serialize(back);
        Assert.Equal(first, second);
        Assert.Equal("greeting", back.Root.Props!.TextKey);
        Assert.Equal("Ana", back.Root.Props!.TextArgs!["name"]);
        Assert.Equal("pt-BR", back.Localization!.DefaultLocale);
    }
}
