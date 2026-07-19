using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Wasi.Protocol.DevTools;

// =============================================================================
// DevTools — inspector da árvore SDUI (Onda 🟢).
//
// Produz um DUMP NAVEGÁVEL do descritor: por nó, o tipo (+ se é conhecido pelo
// host), o id semântico, as props presentes, os TOKENS RESOLVIDOS (cor/estilo/
// espaçamento pelo tema ativo + texto localizado pelo locale ativo) e o ESTADO
// inicial dos inputs. Útil pra debugar "por que este nó ficou assim" sem abrir
// um host nativo.
//
// Puro e platform-neutral: não depende de nenhum host. Dois formatos de saída:
//   • ToText — árvore indentada legível (│ ├─), pra console/HMR.
//   • ToJson — árvore hierárquica (camelCase, omite null), pra ferramentas.
//
// A resolução de tema/i18n espelha EXATAMENTE SduiThemeResolver/SduiLocalizer —
// o inspector mostra o que o host REALMENTE renderiza, não o valor cru.
// =============================================================================

/// <summary>Opções do inspector: versão do host + tema/locale ativos + o que resolver.</summary>
public sealed record SduiInspectorOptions
{
    /// Versão de schema que o "host" simulado entende (define fallback por nó).
    public int HostSchemaVersion { get; init; } = SduiSchema.CurrentVersion;

    /// Modo de tema ativo (System usa SystemPrefersDark).
    public SduiThemeMode? ThemeMode { get; init; }

    /// Preferência do SO quando ThemeMode = System.
    public bool SystemPrefersDark { get; init; }

    /// Locale ativo (ex.: "pt-BR"). null ⇒ usa DefaultLocale/Text cru.
    public string? Locale { get; init; }

    /// Resolve tokens de tema + chaves i18n e mostra os valores efetivos.
    public bool ResolveTokens { get; init; } = true;

    /// Inclui o estado inicial declarado dos inputs (defaultValue/checked).
    public bool IncludeState { get; init; } = true;
}

/// <summary>Um nó no dump do inspector. Serializável (JSON) e renderizável (texto).</summary>
public sealed record SduiInspectorNode
{
    public required string Id { get; init; }
    /// Nome do tipo (ex.: "Card") ou "unknown(200)" quando fora do schema.
    public required string Type { get; init; }
    /// Código byte do tipo no wire.
    public required byte TypeCode { get; init; }
    /// True se o host reconhece o tipo E satisfaz MinSchemaVersion.
    public bool Supported { get; init; }
    /// Versão mínima de schema exigida (quando declarada).
    public int? MinSchemaVersion { get; init; }
    /// Política de fallback efetiva quando não suportado (RenderChildren/Placeholder/Ignore).
    public string? Fallback { get; init; }
    /// Nome da ação de OnTap, quando presente.
    public string? Action { get; init; }
    /// Props presentes (compactas, ordenadas). Ausentes são omitidas.
    public IReadOnlyDictionary<string, string>? Props { get; init; }
    /// Valores efetivos após resolução de tema/i18n/estado (só quando diferem/resolvem).
    public IReadOnlyDictionary<string, string>? Resolved { get; init; }
    public IReadOnlyList<SduiInspectorNode>? Children { get; init; }
}

/// <summary>Árvore completa + metadados de contexto (tema/locale/contagens).</summary>
public sealed record SduiInspectorTree
{
    public int SchemaVersion { get; init; }
    public int HostSchemaVersion { get; init; }
    public string ThemeMode { get; init; } = "System";
    public bool Dark { get; init; }
    public string? Locale { get; init; }
    public int NodeCount { get; init; }
    public int UnsupportedCount { get; init; }
    public IReadOnlyDictionary<string, int> CountsByType { get; init; } = new Dictionary<string, int>();
    public required SduiInspectorNode Root { get; init; }
}

/// <summary>Fachada estática do inspector.</summary>
public static class SduiInspector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SduiInspectorTree Describe(SduiDocument doc, SduiInspectorOptions? options = null)
    {
        var opts = options ?? new SduiInspectorOptions();
        var mode = opts.ThemeMode ?? doc.ThemeMode ?? SduiThemeMode.System;
        var resolver = new SduiThemeResolver(doc.Themes, mode, opts.SystemPrefersDark);
        var localizer = new SduiLocalizer(doc.Localization, opts.Locale);
        bool dark = mode switch
        {
            SduiThemeMode.Dark => true,
            SduiThemeMode.Light => false,
            _ => opts.SystemPrefersDark,
        };

        var counts = new Dictionary<string, int>();
        int total = 0, unsupported = 0;
        var root = Visit(doc.Root, opts, resolver, localizer, counts, ref total, ref unsupported);

        return new SduiInspectorTree
        {
            SchemaVersion = doc.SchemaVersion,
            HostSchemaVersion = opts.HostSchemaVersion,
            ThemeMode = mode.ToString(),
            Dark = dark,
            Locale = opts.Locale,
            NodeCount = total,
            UnsupportedCount = unsupported,
            CountsByType = counts,
            Root = root,
        };
    }

    public static string ToJson(SduiDocument doc, SduiInspectorOptions? options = null) =>
        JsonSerializer.Serialize(Describe(doc, options), Json);

    public static string ToJson(SduiInspectorTree tree) => JsonSerializer.Serialize(tree, Json);

    public static string ToText(SduiDocument doc, SduiInspectorOptions? options = null) =>
        ToText(Describe(doc, options));

    public static string ToText(SduiInspectorTree tree)
    {
        var sb = new StringBuilder();
        sb.Append("SDUI tree  schema=v").Append(tree.SchemaVersion)
          .Append("  host=v").Append(tree.HostSchemaVersion)
          .Append("  theme=").Append(tree.ThemeMode).Append(tree.Dark ? "(dark)" : "(light)");
        if (tree.Locale is not null) sb.Append("  locale=").Append(tree.Locale);
        sb.Append('\n');
        sb.Append("nodes=").Append(tree.NodeCount)
          .Append("  unsupported=").Append(tree.UnsupportedCount).Append('\n');

        WriteText(sb, tree.Root, "", true);
        return sb.ToString();
    }

    // ── interno ────────────────────────────────────────────────────────────────

    private static SduiInspectorNode Visit(
        SduiNode node, SduiInspectorOptions opts,
        SduiThemeResolver resolver, SduiLocalizer localizer,
        Dictionary<string, int> counts, ref int total, ref int unsupported)
    {
        total++;
        byte code = (byte)node.Type;
        bool known = node.Type.IsKnown();
        var typeName = known ? node.Type.ToString() : $"unknown({code})";
        counts[typeName] = counts.GetValueOrDefault(typeName) + 1;

        var fb = node.ResolveFallback(opts.HostSchemaVersion);
        bool supported = fb is null;
        if (!supported) unsupported++;

        var props = BuildProps(node);
        var resolved = opts.ResolveTokens || opts.IncludeState
            ? BuildResolved(node, opts, resolver, localizer)
            : null;

        IReadOnlyList<SduiInspectorNode>? children = null;
        if (node.Children is { Count: > 0 } kids)
        {
            var list = new List<SduiInspectorNode>(kids.Count);
            foreach (var c in kids)
                list.Add(Visit(c, opts, resolver, localizer, counts, ref total, ref unsupported));
            children = list;
        }

        return new SduiInspectorNode
        {
            Id = node.Id,
            Type = typeName,
            TypeCode = code,
            Supported = supported,
            MinSchemaVersion = node.MinSchemaVersion,
            Fallback = supported ? null : fb!.Value.ToString(),
            Action = node.OnTap?.Name,
            Props = props.Count > 0 ? props : null,
            Resolved = resolved is { Count: > 0 } ? resolved : null,
            Children = children,
        };
    }

    private static Dictionary<string, string> BuildProps(SduiNode node)
    {
        var d = new Dictionary<string, string>();
        var p = node.Props;
        if (p is not null)
        {
            Add(d, "text", p.Text);
            Add(d, "textKey", p.TextKey);
            Add(d, "placeholder", p.Placeholder);
            Add(d, "src", p.Src);
            Add(d, "field", p.Field);
            AddF(d, "fontSize", p.FontSize);
            if (p.Weight is { } w) d["weight"] = w.ToString();
            AddColor(d, "color", p.Color);
            AddColor(d, "background", p.Background);
            AddColor(d, "borderColor", p.BorderColor);
            Add(d, "colorToken", p.ColorToken);
            Add(d, "backgroundToken", p.BackgroundToken);
            Add(d, "borderColorToken", p.BorderColorToken);
            Add(d, "textStyle", p.TextStyle);
            Add(d, "spacingToken", p.SpacingToken);
            AddF(d, "spacing", p.Spacing);
            AddF(d, "cornerRadius", p.CornerRadius);
            AddF(d, "width", p.Width);
            AddF(d, "height", p.Height);
            AddF(d, "flex", p.Flex);
            AddF(d, "value", p.Value);
            AddF(d, "min", p.Min);
            AddF(d, "max", p.Max);
            AddF(d, "step", p.Step);
            if (p.Align is { } a) d["align"] = a.ToString();
            if (p.Axis is { } ax) d["axis"] = ax.ToString();
            if (p.Columns is { } cols) d["columns"] = cols.ToString(CultureInfo.InvariantCulture);
            if (p.Checked is { } ch) d["checked"] = ch ? "true" : "false";
            if (p.Disabled is { } dis) d["disabled"] = dis ? "true" : "false";
            if (p.Presented is { } pr) d["presented"] = pr ? "true" : "false";
            if (p.Options is { Count: > 0 } o) d["options"] = o.Count.ToString(CultureInfo.InvariantCulture);
            if (p.Padding is { } pad) d["padding"] = $"{F(pad.Top)},{F(pad.Right)},{F(pad.Bottom)},{F(pad.Left)}";
        }
        if (node.MinSchemaVersion is { } msv) d["minSchema"] = msv.ToString(CultureInfo.InvariantCulture);
        return d;
    }

    private static Dictionary<string, string> BuildResolved(
        SduiNode node, SduiInspectorOptions opts, SduiThemeResolver resolver, SduiLocalizer localizer)
    {
        var d = new Dictionary<string, string>();
        var p = node.Props;

        if (opts.ResolveTokens)
        {
            // Texto localizado (só quando difere do Text cru ou vem de chave).
            var text = localizer.ResolveNode(p);
            if (text is not null && (p?.TextKey is not null) && text != p?.Text)
                d["text"] = text;

            var placeholder = localizer.ResolvePlaceholder(p);
            if (placeholder is not null && p?.PlaceholderKey is not null && placeholder != p?.Placeholder)
                d["placeholder"] = placeholder;

            // Cores efetivas (token → hex). Só quando havia token.
            if (p?.BackgroundToken is not null && resolver.Color(p.BackgroundToken) is { } bg)
                d["background"] = Hex(bg);
            if (p?.ColorToken is not null && resolver.Color(p.ColorToken) is { } col)
                d["color"] = Hex(col);
            if (p?.BorderColorToken is not null && resolver.Color(p.BorderColorToken) is { } bc)
                d["borderColor"] = Hex(bc);
            if (p?.SpacingToken is not null && resolver.Spacing(p.SpacingToken) is { } sp)
                d["spacing"] = F(sp);
            if (p?.TextStyle is not null && resolver.TextStyle(p.TextStyle) is { } ts)
            {
                var parts = new List<string>();
                if (ts.FontSize is { } fs) parts.Add($"size={F(fs)}");
                if (ts.Weight is { } wt) parts.Add($"weight={wt}");
                var tsColor = ts.ColorToken is not null ? resolver.Color(ts.ColorToken) : ts.Color;
                if (tsColor is { } tc) parts.Add($"color={Hex(tc)}");
                if (parts.Count > 0) d["textStyle"] = string.Join(" ", parts);
            }
        }

        if (opts.IncludeState && IsInput(node.Type) && p is not null)
        {
            if (p.DefaultValue is { } dv) d["state.value"] = dv;
            if (p.Checked is { } ck) d["state.checked"] = ck ? "true" : "false";
        }

        return d;
    }

    private static bool IsInput(SduiNodeType t) => t is
        SduiNodeType.TextField or SduiNodeType.Select or SduiNodeType.Checkbox or
        SduiNodeType.Switch or SduiNodeType.Slider or SduiNodeType.Stepper;

    private static void WriteText(StringBuilder sb, SduiInspectorNode n, string prefix, bool root)
    {
        sb.Append(n.Type).Append(" #").Append(n.Id);
        if (!n.Supported) sb.Append("  [fallback=").Append(n.Fallback).Append(']');
        if (n.Action is not null) sb.Append("  onTap=").Append(n.Action);
        AppendKv(sb, n.Props, "");
        AppendKv(sb, n.Resolved, "→");
        sb.Append('\n');

        if (n.Children is { Count: > 0 } kids)
        {
            for (int i = 0; i < kids.Count; i++)
            {
                bool last = i == kids.Count - 1;
                sb.Append(prefix).Append(last ? "└─ " : "├─ ");
                WriteText(sb, kids[i], prefix + (last ? "   " : "│  "), false);
            }
        }
    }

    private static void AppendKv(StringBuilder sb, IReadOnlyDictionary<string, string>? kv, string tag)
    {
        if (kv is null || kv.Count == 0) return;
        foreach (var k in kv.Keys.OrderBy(x => x, StringComparer.Ordinal))
            sb.Append("  ").Append(tag).Append(k).Append('=').Append(Quote(kv[k]));
    }

    private static string Quote(string v) =>
        v.Length == 0 || v.Contains(' ') || v.Contains('=') ? $"\"{v}\"" : v;

    private static void Add(Dictionary<string, string> d, string k, string? v)
    {
        if (v is not null) d[k] = v;
    }

    private static void AddF(Dictionary<string, string> d, string k, float? v)
    {
        if (v is { } f) d[k] = F(f);
    }

    private static void AddColor(Dictionary<string, string> d, string k, uint? v)
    {
        if (v is { } c) d[k] = Hex(c);
    }

    private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Hex(uint rgba) => "#" + rgba.ToString("X8", CultureInfo.InvariantCulture);
}
