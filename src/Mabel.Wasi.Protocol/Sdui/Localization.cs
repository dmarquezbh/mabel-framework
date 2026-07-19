using System.Text;

namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// i18n / l10n NO descritor (Onda 🟡).
//
// As strings de UI saem do descritor e vão pra uma TABELA por locale. Os nós de
// texto carregam uma CHAVE (Props.TextKey) + args (Props.TextArgs) em vez do
// texto final; o host resolve pela tabela do locale ativo e interpola. Trocar de
// idioma = trocar o locale ativo, sem novo descritor.
//
// Fallback em cadeia: locale exato → locale-base (pt-BR → pt) → DefaultLocale →
// o Text cru do nó → a própria chave. Nunca "some" texto.
//
// Interpolação: placeholders {nome} substituídos por TextArgs["nome"]. Suporte a
// pluralização simples via sufixos de chave (".one"/".other") quando TextArgs
// traz "count".
// =============================================================================

/// <summary>
/// Tabela de strings localizáveis. `Locales` mapeia código de locale ("pt-BR",
/// "en") → (chave → template). `DefaultLocale` é o fallback final.
/// </summary>
public sealed record SduiLocalization
{
    /// Locale de fallback quando o ativo não tem a chave (ex.: "pt-BR").
    public string? DefaultLocale { get; init; }

    /// locale → (chave → template com placeholders {nome}).
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Locales { get; init; }
}

/// <summary>
/// Resolvedor de strings localizadas. Puro e testável: espelha a resolução que
/// cada host faz nativamente. Imutável por locale ativo.
/// </summary>
public sealed class SduiLocalizer
{
    private readonly SduiLocalization? _l10n;
    private readonly string? _locale;

    public SduiLocalizer(SduiLocalization? l10n, string? activeLocale)
    {
        _l10n = l10n;
        _locale = activeLocale;
    }

    /// <summary>
    /// Resolve uma chave no locale ativo com fallback em cadeia e interpolação de
    /// args. `fallbackText` = o Text cru do nó (usado se a chave não existe em
    /// nenhum locale). Retorna a própria chave em último caso.
    /// </summary>
    public string Resolve(string key, IReadOnlyDictionary<string, string>? args = null, string? fallbackText = null)
    {
        var template = Lookup(PluralizeKey(key, args)) ?? Lookup(key) ?? fallbackText ?? key;
        return Interpolate(template, args);
    }

    /// <summary>
    /// Resolve o texto de um nó: se Props.TextKey presente, localiza; senão usa
    /// Props.Text cru. Conveniência pros hosts (mesma regra do ResolveText nativo).
    /// </summary>
    public string? ResolveNode(SduiProps? p)
    {
        if (p is null) return null;
        if (p.TextKey is { } key) return Resolve(key, p.TextArgs, p.Text);
        return p.Text;
    }

    /// <summary>Resolve o placeholder de um input (PlaceholderKey → Placeholder).</summary>
    public string? ResolvePlaceholder(SduiProps? p)
    {
        if (p is null) return null;
        if (p.PlaceholderKey is { } key) return Resolve(key, p.TextArgs, p.Placeholder);
        return p.Placeholder;
    }

    // ── interno ────────────────────────────────────────────────────────────────

    /// Busca a chave crua no locale ativo → locale-base → DefaultLocale.
    private string? Lookup(string key)
    {
        if (_l10n?.Locales is not { } locales) return null;

        foreach (var candidate in LocaleChain())
            if (locales.TryGetValue(candidate, out var table) && table.TryGetValue(key, out var v))
                return v;
        return null;
    }

    /// Cadeia de locales a tentar: exato, base (pt-BR→pt), default.
    private IEnumerable<string> LocaleChain()
    {
        if (_locale is { } l)
        {
            yield return l;
            int dash = l.IndexOf('-');
            if (dash > 0) yield return l[..dash];
        }
        if (_l10n?.DefaultLocale is { } d && d != _locale) yield return d;
    }

    /// Pluralização simples: "key" + ".one"/".other" conforme args["count"].
    /// Regra genérica (count == 1 ⇒ one; senão other) — suficiente pra pt/en.
    private static string PluralizeKey(string key, IReadOnlyDictionary<string, string>? args)
    {
        if (args is null || !args.TryGetValue("count", out var raw)) return key;
        bool one = int.TryParse(raw, out var n) && n == 1;
        return key + (one ? ".one" : ".other");
    }

    /// Substitui {nome} por args["nome"]. Chaves ausentes ficam literais.
    private static string Interpolate(string template, IReadOnlyDictionary<string, string>? args)
    {
        if (args is null || template.IndexOf('{') < 0) return template;

        var sb = new StringBuilder(template.Length);
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    var name = template[(i + 1)..end];
                    sb.Append(args.TryGetValue(name, out var val) ? val : template[i..(end + 1)]);
                    i = end;
                    continue;
                }
            }
            sb.Append(template[i]);
        }
        return sb.ToString();
    }
}
