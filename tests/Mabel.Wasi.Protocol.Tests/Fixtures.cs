using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Documentos SDUI de fixture compartilhados pelos testes de DevTools (inspector,
/// snapshot, error boundary). Cobrem theming (tokens), i18n (chaves), forms
/// (estado inicial) e um nó tipo-200 com fallback.
/// </summary>
internal static class Fixtures
{
    /// Documento v3 rico: tema claro/escuro + i18n pt/en + form + tipo-200.
    public static SduiDocument Rich() => new()
    {
        SchemaVersion = 3,
        ThemeMode = SduiThemeMode.System,
        Themes = new SduiThemeSet
        {
            Light = new SduiTheme
            {
                Colors = new Dictionary<string, uint>
                {
                    ["surface"] = 0xFFFFFFFFu,
                    ["onSurface"] = 0x111111FFu,
                    ["primary"] = 0x2D6CDFFFu,
                },
                Spacing = new Dictionary<string, float> { ["md"] = 12f },
                Text = new Dictionary<string, SduiTextStyle>
                {
                    ["title"] = new() { FontSize = 20, Weight = SduiFontWeight.Bold, ColorToken = "onSurface" },
                },
            },
            Dark = new SduiTheme
            {
                Colors = new Dictionary<string, uint>
                {
                    ["surface"] = 0x1A1A2EFFu,
                    ["onSurface"] = 0xEDEDEDFFu,
                    ["primary"] = 0x5B8DEFFFu,
                },
                Spacing = new Dictionary<string, float> { ["md"] = 12f },
                Text = new Dictionary<string, SduiTextStyle>
                {
                    ["title"] = new() { FontSize = 20, Weight = SduiFontWeight.Bold, ColorToken = "onSurface" },
                },
            },
        },
        Localization = new SduiLocalization
        {
            DefaultLocale = "pt-BR",
            Locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["pt-BR"] = new Dictionary<string, string>
                {
                    ["greeting"] = "Olá, {name}",
                    ["field.name"] = "Nome",
                },
                ["en"] = new Dictionary<string, string>
                {
                    ["greeting"] = "Hello, {name}",
                    ["field.name"] = "Name",
                },
            },
        },
        Root = new SduiNode
        {
            Id = "root",
            Type = SduiNodeType.Screen,
            Props = new SduiProps { BackgroundToken = "surface", SpacingToken = "md" },
            Children =
            [
                new SduiNode
                {
                    Id = "title",
                    Type = SduiNodeType.Text,
                    Props = new SduiProps
                    {
                        TextKey = "greeting",
                        TextArgs = new Dictionary<string, string> { ["name"] = "Daniel" },
                        Text = "greeting",
                        TextStyle = "title",
                        ColorToken = "onSurface",
                    },
                },
                new SduiNode
                {
                    Id = "name",
                    Type = SduiNodeType.TextField,
                    Props = new SduiProps
                    {
                        Field = "name",
                        PlaceholderKey = "field.name",
                        Placeholder = "field.name",
                        DefaultValue = "",
                        BackgroundToken = "surface",
                    },
                    Validation = [new SduiValidationRule { Kind = SduiValidationKind.Required }],
                },
                new SduiNode
                {
                    Id = "submit",
                    Type = SduiNodeType.Button,
                    Props = new SduiProps { Text = "OK", ColorToken = "primary" },
                    OnTap = new SduiAction("submit"),
                },
                // Nó do futuro: tipo-200 com fallback de placeholder.
                new SduiNode { Id = "future", Type = (SduiNodeType)200, Fallback = SduiUnknownFallback.Placeholder },
            ],
        },
    };
}
