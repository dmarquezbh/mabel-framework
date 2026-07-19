using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Error boundaries por subárvore. Prova o ISOLAMENTO: um nó que falha ao
/// renderizar (probe do host lança) é substituído por um placeholder de erro
/// SEM derrubar os irmãos nem a tela; a telemetria registra a falha; e o conceito
/// estende o fallback tipo-200 (nó desconhecido) pra qualquer exceção.
/// </summary>
public class SduiErrorBoundaryTests
{
    private static SduiNode Screen(params SduiNode[] children) => new()
    {
        Id = "screen",
        Type = SduiNodeType.Screen,
        Children = children,
    };

    private static SduiNode Text(string id, string txt) => new()
    {
        Id = id,
        Type = SduiNodeType.Text,
        Props = new SduiProps { Text = txt },
    };

    [Fact]
    public void FailingNode_IsIsolated_SiblingsSurvive()
    {
        var tree = Screen(
            Text("ok1", "A"),
            new SduiNode
            {
                Id = "bad",
                Type = SduiNodeType.Card,
                Children = [Text("bad.child", "should not render")],
            },
            Text("ok2", "B"));

        var sink = new SduiCollectingErrorSink();
        // Probe do host: lança só pro nó "bad".
        var guarded = SduiErrorBoundary.Guard(
            tree,
            probe: n => { if (n.Id == "bad") throw new InvalidOperationException("boom"); },
            sink: sink)!;

        var ids = guarded.Children!.Select(c => c.Id).ToArray();
        Assert.Equal(["ok1", "bad!error", "ok2"], ids);

        // A subárvore do nó ruim foi isolada: o filho original não entra.
        var placeholder = guarded.Children!.Single(c => c.Id == "bad!error");
        Assert.Equal(SduiNodeType.Card, placeholder.Type);
        Assert.Equal("true", placeholder.Props!.Data!["mabel.error"]);
        Assert.Equal("bad", placeholder.Props!.Data!["mabel.error.node"]);
        Assert.DoesNotContain(Descendants(placeholder), n => n.Id == "bad.child");

        // Telemetria: uma falha registrada, do tipo RenderFailure.
        Assert.Single(sink.Errors);
        Assert.Equal("bad", sink.Errors[0].NodeId);
        Assert.Equal(SduiErrorKind.RenderFailure, sink.Errors[0].Kind);
        Assert.Equal("boom", sink.Errors[0].Message);
        Assert.Equal(nameof(InvalidOperationException), sink.Errors[0].ExceptionType);
    }

    [Fact]
    public void InvalidData_IsClassified_Distinctly()
    {
        var tree = Screen(Text("ok", "A"), Text("bad", "B"));
        var sink = new SduiCollectingErrorSink();

        SduiErrorBoundary.Guard(
            tree,
            probe: n => { if (n.Id == "bad") throw new SduiInvalidNodeException("valor fora do range"); },
            sink: sink);

        Assert.Equal(SduiErrorKind.InvalidData, sink.Errors[0].Kind);
        Assert.Equal("valor fora do range", sink.Errors[0].Message);
    }

    [Fact]
    public void UnknownType_BecomesErrorPlaceholder_ViaCompat()
    {
        // Tipo-200 com fallback Placeholder: sem probe, o boundary ainda isola
        // via compat e registra telemetria (kind=UnknownType).
        var tree = Screen(
            Text("ok", "A"),
            new SduiNode { Id = "future", Type = (SduiNodeType)200, Fallback = SduiUnknownFallback.Placeholder });

        var sink = new SduiCollectingErrorSink();
        var guarded = SduiErrorBoundary.Guard(tree, probe: null, sink: sink)!;

        Assert.Equal("future!error", guarded.Children![1].Id);
        Assert.Equal(SduiErrorKind.UnknownType, sink.Errors[0].Kind);
    }

    [Fact]
    public void IgnoreFallback_DropsNode_NoPlaceholder()
    {
        var tree = Screen(
            Text("ok", "A"),
            new SduiNode { Id = "gone", Type = (SduiNodeType)201, Fallback = SduiUnknownFallback.Ignore });

        var sink = new SduiCollectingErrorSink();
        var guarded = SduiErrorBoundary.Guard(tree, probe: null, sink: sink)!;

        Assert.Single(guarded.Children!);
        Assert.Equal("ok", guarded.Children![0].Id);
        Assert.False(sink.HasErrors); // ignore silencioso não é erro.
    }

    [Fact]
    public void RenderChildren_KeepsTransparentContainer_ChildrenGuarded()
    {
        // Nó desconhecido com fallback RenderChildren (default): vira container
        // transparente e os filhos seguem — inclusive guardados (um filho ruim
        // é isolado sem afetar o irmão).
        var tree = Screen(new SduiNode
        {
            Id = "wrapper",
            Type = (SduiNodeType)202,
            Fallback = SduiUnknownFallback.RenderChildren,
            Children = [Text("in1", "A"), Text("in2", "B")],
        });

        var sink = new SduiCollectingErrorSink();
        var guarded = SduiErrorBoundary.Guard(
            tree,
            probe: n => { if (n.Id == "in1") throw new Exception("x"); },
            sink: sink)!;

        var wrapper = guarded.Children!.Single(c => c.Id == "wrapper");
        var childIds = wrapper.Children!.Select(c => c.Id).ToArray();
        Assert.Equal(["in1!error", "in2"], childIds);
        Assert.Single(sink.Errors);
    }

    [Fact]
    public void ProductionMode_DropsFailures_TelemetryOnly()
    {
        var tree = Screen(Text("ok", "A"), new SduiNode { Id = "bad", Type = SduiNodeType.Card });
        var sink = new SduiCollectingErrorSink();

        var guarded = SduiErrorBoundary.Guard(
            tree,
            probe: n => { if (n.Id == "bad") throw new Exception("boom"); },
            sink: sink,
            options: new SduiErrorBoundaryOptions { RenderErrorPlaceholders = false })!;

        Assert.Single(guarded.Children!);
        Assert.Equal("ok", guarded.Children![0].Id);
        Assert.Single(sink.Errors); // falha registrada, mas nada exibido.
    }

    [Fact]
    public void GuardDocument_NeverReturnsNullRoot()
    {
        var doc = new SduiDocument
        {
            SchemaVersion = 3,
            Root = new SduiNode { Id = "r", Type = SduiNodeType.Screen },
        };

        var guarded = SduiErrorBoundary.GuardDocument(
            doc, probe: _ => throw new Exception("everything fails"));

        Assert.NotNull(guarded.Root);
        Assert.Equal("r!error", guarded.Root.Id);
    }

    private static IEnumerable<SduiNode> Descendants(SduiNode n)
    {
        if (n.Children is null) yield break;
        foreach (var c in n.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
