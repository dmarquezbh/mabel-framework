using System.Globalization;
using System.Text;
using Mabel.Wasi.Protocol.Sdui;

namespace Mabel.Wasi.Protocol.DevTools;

// =============================================================================
// Testing framework — snapshot do descritor cross-host (Onda 🟢).
//
// Serializa a árvore RENDERIZADA (semântica, não pixel) num formato TEXTUAL
// ESTÁVEL e determinístico, pra comparar com um baseline versionado. Pega
// regressão de layout/binding/tema/i18n sem depender de nenhum host nativo:
// dado (documento + config de host = tema/locale/versão), a árvore semântica
// resolvida é a MESMA em qualquer plataforma — é justamente o contrato SDUI.
//
// O snapshot reflete a resolução REAL dos hosts:
//   • tema: tokens → cores efetivas (hex);
//   • i18n: chaves → texto localizado do locale ativo;
//   • compat: nós não suportados viram !placeholder (isolado) / ~container
//     (transparente, filhos seguem) / são omitidos (ignore).
//
// Determinismo: props ordenadas alfabeticamente, números InvariantCulture, sem
// timestamps. Duas capturas do mesmo input são byte-idênticas.
//
// A mecânica de baseline/arquivo (ler, comparar, atualizar) vive no PROJETO DE
// TESTE (SnapshotAssert); aqui fica só o serializador puro.
// =============================================================================

/// <summary>Serializador de snapshot semântico do descritor SDUI.</summary>
public static class SduiSnapshot
{
    /// <summary>
    /// Captura a árvore resolvida como string canônica. <paramref name="options"/>
    /// reusa SduiInspectorOptions (versão do host + tema + locale).
    /// </summary>
    public static string Capture(SduiDocument doc, SduiInspectorOptions? options = null)
    {
        var opts = options ?? new SduiInspectorOptions();
        var mode = opts.ThemeMode ?? doc.ThemeMode ?? SduiThemeMode.System;
        var resolver = new SduiThemeResolver(doc.Themes, mode, opts.SystemPrefersDark);
        var localizer = new SduiLocalizer(doc.Localization, opts.Locale);

        var sb = new StringBuilder();
        sb.Append("# sdui-snapshot v").Append(doc.SchemaVersion)
          .Append(" host=v").Append(opts.HostSchemaVersion)
          .Append(mode == SduiThemeMode.Dark || (mode == SduiThemeMode.System && opts.SystemPrefersDark) ? " dark" : " light");
        if (opts.Locale is not null) sb.Append(" locale=").Append(opts.Locale);
        sb.Append('\n');

        Write(sb, doc.Root, 0, opts, resolver, localizer);
        return sb.ToString();
    }

    private static void Write(
        StringBuilder sb, SduiNode node, int depth,
        SduiInspectorOptions opts, SduiThemeResolver resolver, SduiLocalizer localizer)
    {
        var fb = node.ResolveFallback(opts.HostSchemaVersion);
        var indent = new string(' ', depth * 2);

        if (fb is { } policy)
        {
            switch (policy)
            {
                case SduiUnknownFallback.Ignore:
                    return; // omitido — não ocupa espaço no snapshot.

                case SduiUnknownFallback.Placeholder:
                    sb.Append(indent).Append("!placeholder id=").Append(node.Id)
                      .Append(" type=").Append((byte)node.Type)
                      .Append(!node.Type.IsKnown() ? " reason=unknown-type"
                          : $" reason=needs-schema-v{node.MinSchemaVersion}")
                      .Append('\n');
                    return; // subárvore isolada — filhos NÃO entram.

                case SduiUnknownFallback.RenderChildren:
                default:
                    sb.Append(indent).Append("~container id=").Append(node.Id)
                      .Append(" type=").Append((byte)node.Type).Append('\n');
                    WriteChildren(sb, node, depth + 1, opts, resolver, localizer);
                    return;
            }
        }

        // Nó suportado: tipo + id + props semânticas ordenadas.
        sb.Append(indent).Append(node.Type).Append(" id=").Append(node.Id);
        foreach (var (k, v) in ResolvedProps(node, opts, resolver, localizer))
            sb.Append(' ').Append(k).Append('=').Append(v);
        sb.Append('\n');

        WriteChildren(sb, node, depth + 1, opts, resolver, localizer);
    }

    private static void WriteChildren(
        StringBuilder sb, SduiNode node, int depth,
        SduiInspectorOptions opts, SduiThemeResolver resolver, SduiLocalizer localizer)
    {
        if (node.Children is not { Count: > 0 } kids) return;
        foreach (var c in kids)
            Write(sb, c, depth, opts, resolver, localizer);
    }

    /// <summary>Props semânticas resolvidas, ordenadas (determinístico).</summary>
    private static IEnumerable<(string, string)> ResolvedProps(
        SduiNode node, SduiInspectorOptions opts, SduiThemeResolver resolver, SduiLocalizer localizer)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var p = node.Props;

        // Texto: localizado (i18n) tem prioridade sobre o cru.
        var text = localizer.ResolveNode(p);
        if (!string.IsNullOrEmpty(text)) d["text"] = Q(text);

        var placeholder = localizer.ResolvePlaceholder(p);
        if (!string.IsNullOrEmpty(placeholder)) d["placeholder"] = Q(placeholder);

        if (p is not null)
        {
            // Cores efetivas (token → hex, senão valor cru).
            if (resolver.ResolveBackground(p) is { } bg) d["bg"] = Hex(bg);
            if (resolver.ResolveColor(p) is { } fg) d["fg"] = Hex(fg);
            if (resolver.ResolveBorderColor(p) is { } bc) d["border"] = Hex(bc);

            // Espaçamento efetivo (token → valor, senão cru).
            var spacing = (p.SpacingToken is not null ? resolver.Spacing(p.SpacingToken) : null) ?? p.Spacing;
            if (spacing is { } sp) d["spacing"] = F(sp);

            if (p.Axis is { } ax) d["axis"] = ax.ToString();
            if (p.Align is { } al) d["align"] = al.ToString();
            if (p.Columns is { } cols) d["columns"] = cols.ToString(CultureInfo.InvariantCulture);
            if (p.Width is { } w) d["w"] = F(w);
            if (p.Height is { } h) d["h"] = F(h);
            if (p.CornerRadius is { } cr) d["radius"] = F(cr);
            if (p.Value is { } val) d["value"] = F(val);
            if (p.Src is { } src) d["src"] = Q(src);
            if (p.Field is { } f) d["field"] = Q(f);
            if (p.DefaultValue is { } dv) d["default"] = Q(dv);
            if (p.Checked is { } ck) d["checked"] = ck ? "true" : "false";
            if (p.Disabled is { } dis) d["disabled"] = dis ? "true" : "false";
            if (p.Presented is { } pr) d["presented"] = pr ? "true" : "false";
            if (p.Options is { Count: > 0 } o) d["options"] = o.Count.ToString(CultureInfo.InvariantCulture);
            if (p.Padding is { } pad) d["padding"] = $"{F(pad.Top)},{F(pad.Right)},{F(pad.Bottom)},{F(pad.Left)}";
        }

        // Semântica transversal relevante ao layout/binding.
        if (node.OnTap is { } tap) d["onTap"] = Q(tap.Name);
        if (node.List is not null) d["list"] = "virtualized";
        if (node.Tabs is { Count: > 0 } tabs) d["tabs"] = tabs.Count.ToString(CultureInfo.InvariantCulture);
        if (node.Validation is { Count: > 0 } val2) d["validation"] = val2.Count.ToString(CultureInfo.InvariantCulture);

        return d.Select(kv => (kv.Key, kv.Value));
    }

    private static string Q(string v) =>
        v.Length == 0 || v.Contains(' ') || v.Contains('=') || v.Contains('"')
            ? "\"" + v.Replace("\"", "\\\"") + "\"" : v;

    private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Hex(uint rgba) => "#" + rgba.ToString("X8", CultureInfo.InvariantCulture);
}
