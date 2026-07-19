using System.Globalization;
using System.Text.RegularExpressions;

namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Forms + validação declarativa (Onda 🟡).
//
// Os nós de input (TextField/Select/Checkbox/Switch/Slider/Stepper) declaram um
// Field (nome no modelo) via Props.Field e regras de validação (SduiNode.
// Validation). O host renderiza o controle nativo, faz o two-way binding com o
// estado do formulário e exibe o estado de erro. A AVALIAÇÃO das regras é pura
// (SduiValidator) — o mesmo veredito em qualquer plataforma.
//
// Regras platform-neutral e serializáveis; mensagens localizáveis (MessageKey).
// =============================================================================

/// <summary>Uma opção de um Select. Value = valor submetido; Label/LabelKey = exibição.</summary>
public sealed record SduiOption
{
    public required string Value { get; init; }
    /// Rótulo exibido (cru). Ausente ⇒ o host mostra Value.
    public string? Label { get; init; }
    /// Rótulo localizável (i18n). Vence Label quando resolvido.
    public string? LabelKey { get; init; }
}

/// <summary>Tipo de regra de validação. Byte-enum (decode UInt8 no host Swift).</summary>
public enum SduiValidationKind : byte
{
    /// Valor não pode ser vazio/ausente.
    Required = 0,
    /// Comprimento mínimo (Param = int).
    MinLength = 1,
    /// Comprimento máximo (Param = int).
    MaxLength = 2,
    /// Casa a regex de Param.
    Pattern = 3,
    /// Valor numérico ≥ Param.
    Min = 4,
    /// Valor numérico ≤ Param.
    Max = 5,
    /// Formato de e-mail.
    Email = 6,
}

/// <summary>
/// Uma regra de validação de um campo. `Param` carrega o argumento textual
/// (tamanho, regex, limite) conforme o Kind. `Message`/`MessageKey` = erro exibido.
/// </summary>
public sealed record SduiValidationRule
{
    public required SduiValidationKind Kind { get; init; }
    /// Argumento da regra (ex.: "3" p/ MinLength, "^\\d+$" p/ Pattern). Interpretação
    /// depende do Kind; ignorado por Required/Email.
    public string? Param { get; init; }
    /// Mensagem de erro crua.
    public string? Message { get; init; }
    /// Mensagem de erro localizável (i18n). Vence Message quando resolvida.
    public string? MessageKey { get; init; }
}

/// <summary>Erro de validação de um campo (resultado de SduiValidator).</summary>
public sealed record SduiFieldError(string Field, SduiValidationKind Kind, string Message);

/// <summary>
/// Motor de validação puro. Avalia as regras de um campo (ou de uma árvore de
/// inputs) contra os valores atuais e devolve os erros. Sem dependência de UI —
/// testável isoladamente e reutilizável por qualquer host.
/// </summary>
public static class SduiValidator
{
    // E-mail pragmático (não RFC-5322 completo): algo@algo.tld, sem espaços.
    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Avalia UMA regra contra um valor. null se passa; senão a mensagem de erro.
    /// `messageResolver` (opcional) localiza rule.MessageKey.
    /// </summary>
    public static string? Evaluate(SduiValidationRule rule, string? value, Func<string, string>? messageResolver = null)
    {
        bool ok = rule.Kind switch
        {
            SduiValidationKind.Required  => !string.IsNullOrWhiteSpace(value),
            SduiValidationKind.MinLength => (value?.Length ?? 0) >= ParseInt(rule.Param),
            SduiValidationKind.MaxLength => (value?.Length ?? 0) <= ParseInt(rule.Param, int.MaxValue),
            SduiValidationKind.Pattern   => value is null || rule.Param is null || Regex.IsMatch(value, rule.Param),
            SduiValidationKind.Min       => !TryNum(value, out var n) || n >= ParseNum(rule.Param, double.MinValue),
            SduiValidationKind.Max       => !TryNum(value, out var n) || n <= ParseNum(rule.Param, double.MaxValue),
            SduiValidationKind.Email     => string.IsNullOrEmpty(value) || EmailRegex.IsMatch(value),
            _ => true,
        };
        return ok ? null : ResolveMessage(rule, messageResolver);
    }

    /// <summary>
    /// Valida um campo (todas as regras, na ordem). Retorna o PRIMEIRO erro, ou
    /// null se todas passam. (Primeiro erro = o que o host tipicamente exibe.)
    /// </summary>
    public static SduiFieldError? ValidateField(
        string field, IReadOnlyList<SduiValidationRule> rules, string? value,
        Func<string, string>? messageResolver = null)
    {
        foreach (var rule in rules)
            if (Evaluate(rule, value, messageResolver) is { } msg)
                return new SduiFieldError(field, rule.Kind, msg);
        return null;
    }

    /// <summary>
    /// Percorre a árvore, encontra todo nó com Props.Field + Validation, e valida
    /// contra `values[field]`. Devolve todos os campos com erro (um por campo).
    /// É o que um host chama no submit pra decidir se bloqueia.
    /// </summary>
    public static IReadOnlyList<SduiFieldError> ValidateTree(
        SduiNode root, IReadOnlyDictionary<string, string?> values,
        Func<string, string>? messageResolver = null)
    {
        var errors = new List<SduiFieldError>();
        Walk(root, values, messageResolver, errors);
        return errors;
    }

    private static void Walk(
        SduiNode node, IReadOnlyDictionary<string, string?> values,
        Func<string, string>? messageResolver, List<SduiFieldError> errors)
    {
        if (node.Props?.Field is { } field && node.Validation is { Count: > 0 } rules)
        {
            values.TryGetValue(field, out var value);
            if (ValidateField(field, rules, value, messageResolver) is { } err)
                errors.Add(err);
        }
        if (node.Children is { } children)
            foreach (var c in children)
                Walk(c, values, messageResolver, errors);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static string ResolveMessage(SduiValidationRule rule, Func<string, string>? resolver)
    {
        if (rule.MessageKey is { } key && resolver is not null) return resolver(key);
        return rule.Message ?? rule.MessageKey ?? DefaultMessage(rule.Kind, rule.Param);
    }

    private static string DefaultMessage(SduiValidationKind kind, string? param) => kind switch
    {
        SduiValidationKind.Required  => "Campo obrigatório.",
        SduiValidationKind.MinLength => $"Mínimo de {param} caracteres.",
        SduiValidationKind.MaxLength => $"Máximo de {param} caracteres.",
        SduiValidationKind.Pattern   => "Formato inválido.",
        SduiValidationKind.Min       => $"Valor mínimo: {param}.",
        SduiValidationKind.Max       => $"Valor máximo: {param}.",
        SduiValidationKind.Email     => "E-mail inválido.",
        _ => "Valor inválido.",
    };

    private static int ParseInt(string? s, int fallback = 0) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static double ParseNum(string? s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool TryNum(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
