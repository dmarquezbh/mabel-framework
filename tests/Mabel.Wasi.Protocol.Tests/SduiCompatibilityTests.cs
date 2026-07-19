using Mabel.Wasi.Protocol.Sdui;
using Xunit;

namespace Mabel.Wasi.Protocol.Tests;

/// <summary>
/// Degradação graciosa / compat OTA: um descritor "do futuro" (schema mais novo,
/// tipos e props que este build não conhece) NÃO pode quebrar o parse. É o que
/// mantém o OTA seguro quando o guest evolui à frente do host instalado.
/// </summary>
public class SduiCompatibilityTests
{
    [Fact]
    public void FutureDescriptor_UnknownNodeType_DoesNotBreakParse()
    {
        // schemaVersion 99, tipo de nó 200 (inexistente aqui) e uma prop desconhecida.
        const string futureJson = """
        {
          "schemaVersion": 99,
          "root": {
            "id": "screen:future",
            "type": 1,
            "children": [
              {
                "id": "widget:hologram",
                "type": 200,
                "minSchemaVersion": 99,
                "fallback": 1,
                "someUnknownProp": { "nested": true },
                "children": [
                  { "id": "fallback:text", "type": 7, "props": { "text": "conteúdo legado" } }
                ]
              }
            ]
          }
        }
        """;

        var doc = SduiJson.Deserialize(futureJson);

        Assert.NotNull(doc);
        Assert.Equal(99, doc!.SchemaVersion);

        var unknown = doc.Root.Children![0];
        // O valor bruto do tipo é preservado, mesmo sem membro nomeado.
        Assert.Equal((SduiNodeType)200, unknown.Type);
        Assert.False(unknown.Type.IsKnown());
        // O filho conhecido continua parseável — a subárvore não foi perdida.
        Assert.Equal(SduiNodeType.Text, unknown.Children![0].Type);
        Assert.Equal("conteúdo legado", unknown.Children![0].Props!.Text);
    }

    [Fact]
    public void UnknownNode_ResolvesDeclaredFallbackPolicy()
    {
        const string json = """
        {
          "schemaVersion": 99,
          "root": {
            "id": "widget:x",
            "type": 200,
            "fallback": 2
          }
        }
        """;

        var doc = SduiJson.Deserialize(json)!;
        // Host na v2 não conhece o tipo 200 → aplica a política declarada (Ignore=2).
        Assert.Equal(SduiUnknownFallback.Ignore, doc.Root.ResolveFallback(hostSchemaVersion: SduiSchema.CurrentVersion));
    }

    [Fact]
    public void KnownNode_NeedingNewerSchema_FallsBackToRenderChildren()
    {
        // Tipo conhecido (Card=6) mas exige schema 5; host na v2 é mais antigo.
        var node = new SduiNode
        {
            Id = "card:new",
            Type = SduiNodeType.Card,
            MinSchemaVersion = 5,
            Children = [new SduiNode { Id = "t", Type = SduiNodeType.Text }],
        };

        // Fallback ausente ⇒ default seguro do contrato = RenderChildren.
        Assert.Equal(SduiUnknownFallback.RenderChildren, node.ResolveFallback(hostSchemaVersion: 2));
        // Host na v5+ suporta plenamente → sem fallback.
        Assert.Null(node.ResolveFallback(hostSchemaVersion: 5));
    }

    [Fact]
    public void KnownNode_OnCurrentSchema_NeedsNoFallback()
    {
        var node = new SduiNode { Id = "t", Type = SduiNodeType.Text };
        Assert.True(node.Type.IsKnown());
        Assert.Null(node.ResolveFallback(hostSchemaVersion: SduiSchema.CurrentVersion));
    }

    [Fact]
    public void UnknownEnumValues_InProps_DoNotBreakParse()
    {
        // Valores de enum fora da faixa nomeada (a11yRole 99, wrap 42) devem
        // deserializar sem exceção — o host os ignora/clampa ao renderizar.
        const string json = """
        {
          "schemaVersion": 99,
          "root": {
            "id": "n",
            "type": 7,
            "a11y": { "role": 99 },
            "props": { "wrap": 42 }
          }
        }
        """;

        var doc = SduiJson.Deserialize(json)!;
        Assert.Equal((SduiA11yRole)99, doc.Root.A11y!.Role);
        Assert.Equal((SduiWrap)42, doc.Root.Props!.Wrap);
    }
}
