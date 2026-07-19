using System.Collections.Concurrent;

namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Error boundaries POR SUBÁRVORE (Onda 🟢).
//
// Estende o conceito de degradação graciosa (Compatibility.cs) de "tipo
// desconhecido" para QUALQUER falha de render. Se um nó falha ao ser preparado
// pelo host (dados inválidos, tipo quebrado, exceção nativa), a subárvore é
// ISOLADA: em vez de derrubar a tela inteira, o boundary substitui aquele nó
// por um PLACEHOLDER DE ERRO (Card + Text, platform-neutral) e emite TELEMETRIA
// (ISduiErrorSink). Os IRMÃOS e o resto da árvore renderizam normalmente.
//
// Fluxo por nó (recursivo, cada nó guardado independentemente):
//   1. Compat: nó não suportado (tipo desconhecido / schema muito novo)
//        • Ignore         → nó removido (não ocupa espaço)
//        • Placeholder    → placeholder de erro no lugar (subárvore isolada)
//        • RenderChildren → nó tratado como container transparente; filhos seguem
//   2. Probe do host: tenta preparar ESTE nó. Lança ⇒ falha isolada:
//        placeholder de erro no lugar + telemetria; a subárvore NÃO é renderizada.
//   3. Filhos: cada um passa pelo boundary; um filho podre não contamina os irmãos.
//
// Puro e testável no WSL: o "probe" é um delegate (o host injeta seu builder);
// nos testes um probe que lança pra um id específico prova o isolamento.
// =============================================================================

/// <summary>Categoria de uma falha de render capturada por um error boundary.</summary>
public enum SduiErrorKind : byte
{
    /// O host lançou ao tentar preparar/renderizar o nó (exceção genérica).
    RenderFailure = 0,
    /// Tipo de nó desconhecido pelo host (fora do schema).
    UnknownType = 1,
    /// Nó exige um schema mais novo que o do host (MinSchemaVersion > host).
    SchemaTooNew = 2,
    /// Dados do nó inválidos (host sinalizou via SduiInvalidNodeException).
    InvalidData = 3,
}

/// <summary>Registro de telemetria de uma subárvore que falhou ao renderizar.</summary>
public sealed record SduiRenderError
{
    public required string NodeId { get; init; }
    public required byte TypeCode { get; init; }
    public required SduiErrorKind Kind { get; init; }
    public required string Message { get; init; }
    /// Nome do tipo da exceção, quando a falha veio de um throw do host.
    public string? ExceptionType { get; init; }
}

/// <summary>Dreno de telemetria de erros. O host liga a log/New Relic/etc.</summary>
public interface ISduiErrorSink
{
    void Report(SduiRenderError error);
}

/// <summary>Sink que acumula os erros em memória (para dev/HML e testes).</summary>
public sealed class SduiCollectingErrorSink : ISduiErrorSink
{
    private readonly ConcurrentQueue<SduiRenderError> _errors = new();
    public void Report(SduiRenderError error) => _errors.Enqueue(error);
    public IReadOnlyList<SduiRenderError> Errors => _errors.ToArray();
    public bool HasErrors => !_errors.IsEmpty;
}

/// <summary>Sink que delega a um callback (ex.: logger). Conveniência pros hosts.</summary>
public sealed class SduiDelegateErrorSink(Action<SduiRenderError> onError) : ISduiErrorSink
{
    public void Report(SduiRenderError error) => onError(error);
}

/// <summary>
/// Exceção que um probe de host pode lançar pra marcar a falha como "dados
/// inválidos" (SduiErrorKind.InvalidData) em vez de falha genérica de render.
/// </summary>
public sealed class SduiInvalidNodeException(string message) : Exception(message);

/// <summary>Opções do error boundary.</summary>
public sealed record SduiErrorBoundaryOptions
{
    /// Versão de schema do host (define quais nós precisam de fallback de compat).
    public int HostSchemaVersion { get; init; } = SduiSchema.CurrentVersion;

    /// Quando true (default), nós que falham viram placeholder VISÍVEL de erro.
    /// Quando false, falhas são removidas silenciosamente (só telemetria) — útil
    /// em produção pra não expor erro ao usuário.
    public bool RenderErrorPlaceholders { get; init; } = true;

    /// Cor de fundo do placeholder de erro (RGBA). Default: vermelho-claro.
    public uint PlaceholderBackground { get; init; } = 0xFFE5E5FFu;

    /// Cor do texto do placeholder de erro (RGBA). Default: vermelho-escuro.
    public uint PlaceholderTextColor { get; init; } = 0xB00020FFu;
}

/// <summary>Fachada estática do error boundary por subárvore.</summary>
public static class SduiErrorBoundary
{
    /// <summary>
    /// Guarda a árvore inteira do documento. Retorna um novo SduiDocument em que
    /// cada subárvore que falharia foi isolada (placeholder + telemetria).
    /// </summary>
    public static SduiDocument GuardDocument(
        SduiDocument doc, Action<SduiNode>? probe = null,
        ISduiErrorSink? sink = null, SduiErrorBoundaryOptions? options = null)
    {
        var opts = options ?? new SduiErrorBoundaryOptions();
        var root = Guard(doc.Root, probe, sink, opts)
            // Se a própria raiz caiu (Ignore), garante uma raiz-placeholder pra
            // não devolver documento sem Root.
            ?? ErrorPlaceholder(doc.Root, SduiErrorKind.RenderFailure, "root dropped", opts);
        return doc with { Root = root };
    }

    /// <summary>
    /// Guarda um nó e sua subárvore. Retorna:
    ///   • o nó (com filhos guardados) quando renderizável;
    ///   • um placeholder de erro quando o nó falha (subárvore isolada);
    ///   • null quando o nó deve ser removido (fallback Ignore, ou falha com
    ///     RenderErrorPlaceholders=false).
    /// </summary>
    public static SduiNode? Guard(
        SduiNode node, Action<SduiNode>? probe = null,
        ISduiErrorSink? sink = null, SduiErrorBoundaryOptions? options = null)
    {
        var opts = options ?? new SduiErrorBoundaryOptions();

        // ── 1. Compat: nó não suportado pelo host ────────────────────────────
        var fb = node.ResolveFallback(opts.HostSchemaVersion);
        if (fb is { } policy)
        {
            switch (policy)
            {
                case SduiUnknownFallback.Ignore:
                    return null;

                case SduiUnknownFallback.Placeholder:
                {
                    var kind = !node.Type.IsKnown() ? SduiErrorKind.UnknownType : SduiErrorKind.SchemaTooNew;
                    var reason = kind == SduiErrorKind.UnknownType
                        ? $"unknown node type {(byte)node.Type}"
                        : $"needs schema v{node.MinSchemaVersion} (host v{opts.HostSchemaVersion})";
                    Report(sink, node, kind, reason, null);
                    return opts.RenderErrorPlaceholders ? ErrorPlaceholder(node, kind, reason, opts) : null;
                }

                case SduiUnknownFallback.RenderChildren:
                default:
                    // Container transparente: NÃO faz probe do nó (o host não o
                    // desenha), mas os filhos ainda passam pelo boundary.
                    return node with { Children = GuardChildren(node, probe, sink, opts) };
            }
        }

        // ── 2. Probe do host: tenta preparar ESTE nó ─────────────────────────
        if (probe is not null)
        {
            try
            {
                probe(node);
            }
            catch (SduiInvalidNodeException ex)
            {
                Report(sink, node, SduiErrorKind.InvalidData, ex.Message, nameof(SduiInvalidNodeException));
                return opts.RenderErrorPlaceholders
                    ? ErrorPlaceholder(node, SduiErrorKind.InvalidData, ex.Message, opts) : null;
            }
            catch (Exception ex)
            {
                Report(sink, node, SduiErrorKind.RenderFailure, ex.Message, ex.GetType().Name);
                return opts.RenderErrorPlaceholders
                    ? ErrorPlaceholder(node, SduiErrorKind.RenderFailure, ex.Message, opts) : null;
            }
        }

        // ── 3. Filhos: cada um guardado independentemente ────────────────────
        return node with { Children = GuardChildren(node, probe, sink, opts) };
    }

    private static IReadOnlyList<SduiNode>? GuardChildren(
        SduiNode node, Action<SduiNode>? probe, ISduiErrorSink? sink, SduiErrorBoundaryOptions opts)
    {
        if (node.Children is not { Count: > 0 } kids) return node.Children;
        var safe = new List<SduiNode>(kids.Count);
        foreach (var c in kids)
        {
            var g = Guard(c, probe, sink, opts);
            if (g is not null) safe.Add(g);
        }
        return safe;
    }

    private static void Report(
        ISduiErrorSink? sink, SduiNode node, SduiErrorKind kind, string message, string? exType)
    {
        sink?.Report(new SduiRenderError
        {
            NodeId = node.Id,
            TypeCode = (byte)node.Type,
            Kind = kind,
            Message = message,
            ExceptionType = exType,
        });
    }

    /// <summary>
    /// Placeholder de erro platform-neutral: um Card estilizado com um Text
    /// descritivo. Data["mabel.error"]=true permite ao host/inspector reconhecê-lo.
    /// Substitui a subárvore inteira (os filhos do nó original NÃO entram).
    /// </summary>
    private static SduiNode ErrorPlaceholder(
        SduiNode original, SduiErrorKind kind, string reason, SduiErrorBoundaryOptions opts)
    {
        var typeName = original.Type.IsKnown() ? original.Type.ToString() : $"type-{(byte)original.Type}";
        return new SduiNode
        {
            Id = original.Id + "!error",
            Type = SduiNodeType.Card,
            Props = new SduiProps
            {
                Background = opts.PlaceholderBackground,
                CornerRadius = 6,
                Padding = SduiEdges.All(8),
                Data = new Dictionary<string, string>
                {
                    ["mabel.error"] = "true",
                    ["mabel.error.node"] = original.Id,
                    ["mabel.error.kind"] = kind.ToString(),
                    ["mabel.error.reason"] = reason,
                },
            },
            Children =
            [
                new SduiNode
                {
                    Id = original.Id + "!error.text",
                    Type = SduiNodeType.Text,
                    Props = new SduiProps
                    {
                        Text = $"⚠ {typeName} '{original.Id}': {reason}",
                        Color = opts.PlaceholderTextColor,
                        FontSize = 12,
                    },
                },
            ],
        };
    }
}
