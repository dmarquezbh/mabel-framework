using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Onda 🟡 — forms + validação declarativa. As regras são avaliadas por
/// SduiValidator (puro, cross-platform). Round-trip preserva inputs, opções e
/// regras.
/// </summary>
public class SduiFormsTests
{
    [Theory]
    [InlineData(SduiValidationKind.Required, null, "", false)]
    [InlineData(SduiValidationKind.Required, null, "x", true)]
    [InlineData(SduiValidationKind.MinLength, "3", "ab", false)]
    [InlineData(SduiValidationKind.MinLength, "3", "abc", true)]
    [InlineData(SduiValidationKind.MaxLength, "3", "abcd", false)]
    [InlineData(SduiValidationKind.MaxLength, "3", "abc", true)]
    [InlineData(SduiValidationKind.Pattern, "^\\d+$", "12a", false)]
    [InlineData(SduiValidationKind.Pattern, "^\\d+$", "123", true)]
    [InlineData(SduiValidationKind.Min, "10", "5", false)]
    [InlineData(SduiValidationKind.Min, "10", "10", true)]
    [InlineData(SduiValidationKind.Max, "10", "11", false)]
    [InlineData(SduiValidationKind.Max, "10", "9", true)]
    [InlineData(SduiValidationKind.Email, null, "notanemail", false)]
    [InlineData(SduiValidationKind.Email, null, "a@b.co", true)]
    public void Validator_Evaluate_SingleRule(SduiValidationKind kind, string? param, string value, bool passes)
    {
        var rule = new SduiValidationRule { Kind = kind, Param = param };
        var error = SduiValidator.Evaluate(rule, value);
        Assert.Equal(passes, error is null);
    }

    [Fact]
    public void Validator_ValidateField_ReturnsFirstError()
    {
        var rules = new[]
        {
            new SduiValidationRule { Kind = SduiValidationKind.Required, Message = "obrigatório" },
            new SduiValidationRule { Kind = SduiValidationKind.MinLength, Param = "5", Message = "curto" },
        };
        var err = SduiValidator.ValidateField("nome", rules, "");
        Assert.NotNull(err);
        Assert.Equal("nome", err!.Field);
        Assert.Equal(SduiValidationKind.Required, err.Kind);
        Assert.Equal("obrigatório", err.Message);

        // Passa Required mas falha MinLength → segundo erro.
        var err2 = SduiValidator.ValidateField("nome", rules, "abc");
        Assert.Equal(SduiValidationKind.MinLength, err2!.Kind);

        // Válido → sem erro.
        Assert.Null(SduiValidator.ValidateField("nome", rules, "abcdef"));
    }

    [Fact]
    public void Validator_MessageKey_ResolvedByLocalizer()
    {
        var l10n = new SduiLocalizer(new SduiLocalization
        {
            Locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["pt-BR"] = new Dictionary<string, string> { ["err.required"] = "Preencha este campo." },
            },
        }, "pt-BR");
        var rule = new SduiValidationRule { Kind = SduiValidationKind.Required, MessageKey = "err.required" };
        var msg = SduiValidator.Evaluate(rule, "", k => l10n.Resolve(k));
        Assert.Equal("Preencha este campo.", msg);
    }

    [Fact]
    public void Validator_ValidateTree_CollectsErrorsPerField()
    {
        var form = new SduiNode
        {
            Id = "form",
            Type = SduiNodeType.VStack,
            Children =
            [
                new SduiNode
                {
                    Id = "email", Type = SduiNodeType.TextField,
                    Props = new SduiProps { Field = "email" },
                    Validation = [new SduiValidationRule { Kind = SduiValidationKind.Email }],
                },
                new SduiNode
                {
                    Id = "age", Type = SduiNodeType.Stepper,
                    Props = new SduiProps { Field = "age" },
                    Validation = [new SduiValidationRule { Kind = SduiValidationKind.Min, Param = "18" }],
                },
                new SduiNode
                {
                    Id = "name", Type = SduiNodeType.TextField,
                    Props = new SduiProps { Field = "name" },
                    Validation = [new SduiValidationRule { Kind = SduiValidationKind.Required }],
                },
            ],
        };
        var values = new Dictionary<string, string?> { ["email"] = "bad", ["age"] = "15", ["name"] = "Ana" };
        var errors = SduiValidator.ValidateTree(form, values);

        Assert.Equal(2, errors.Count); // email + age falham; name passa.
        Assert.Contains(errors, e => e.Field == "email" && e.Kind == SduiValidationKind.Email);
        Assert.Contains(errors, e => e.Field == "age" && e.Kind == SduiValidationKind.Min);
        Assert.DoesNotContain(errors, e => e.Field == "name");
    }

    [Fact]
    public void FormDocument_RoundTripsStable()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = SduiSchema.CurrentVersion,
            Root = new SduiNode
            {
                Id = "form", Type = SduiNodeType.VStack,
                Children =
                [
                    new SduiNode
                    {
                        Id = "uf", Type = SduiNodeType.Select,
                        Props = new SduiProps
                        {
                            Field = "uf",
                            Placeholder = "Selecione",
                            Options =
                            [
                                new SduiOption { Value = "SP", Label = "São Paulo" },
                                new SduiOption { Value = "RJ", LabelKey = "uf.rj" },
                            ],
                        },
                    },
                    new SduiNode
                    {
                        Id = "aceite", Type = SduiNodeType.Checkbox,
                        Props = new SduiProps { Field = "aceite", Checked = false, Text = "Aceito os termos" },
                        Validation = [new SduiValidationRule { Kind = SduiValidationKind.Required, MessageKey = "err.aceite" }],
                    },
                    new SduiNode
                    {
                        Id = "vol", Type = SduiNodeType.Slider,
                        Props = new SduiProps { Field = "vol", Min = 0, Max = 100, Step = 5, DefaultValue = "30" },
                    },
                ],
            },
        };
        var first = SduiJson.Serialize(doc);
        var back = SduiJson.Deserialize(first)!;
        var second = SduiJson.Serialize(back);
        Assert.Equal(first, second);

        var select = back.Root.Children![0];
        Assert.Equal(SduiNodeType.Select, select.Type);
        Assert.Equal(2, select.Props!.Options!.Count);
        Assert.Equal("RJ", select.Props!.Options![1].Value);
        Assert.Equal(SduiValidationKind.Required, back.Root.Children![1].Validation![0].Kind);
    }
}
