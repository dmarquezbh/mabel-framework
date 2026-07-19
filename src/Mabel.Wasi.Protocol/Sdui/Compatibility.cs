using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Versionamento de schema + degradação graciosa (OTA-safe).
//
// O descritor é entregue Over-The-Air: o guest pode emitir uma versão de schema
// MAIS NOVA que o host instalado entende. Sem contrato de degradação, um Type
// novo (ou uma prop nova) quebraria o parse e a tela inteira sumiria.
//
// Regras do contrato:
//   • Props desconhecidas → o host as IGNORA (System.Text.Json já faz isso; o
//     host Swift idem via keys opcionais). Nunca quebram o parse.
//   • Type de nó desconhecido (ou MinSchemaVersion > versão do host) → o host
//     aplica SduiNode.Fallback. Ausente ⇒ RenderChildren (o default seguro:
//     nós novos costumam ser wrappers semânticos sobre filhos já conhecidos).
//
// Este arquivo dá a REPRESENTAÇÃO (SduiUnknownFallback) + os utilitários que
// tornam a regra verificável (IsKnown, negociação de versão) + as opções JSON
// canônicas compartilhadas por emissor e testes.
// =============================================================================

/// <summary>
/// O que um host faz com um nó que ele não reconhece (Type desconhecido ou que
/// exige um schema mais novo que o dele). Byte-enum: espelha o decode UInt8 do
/// host Swift e mantém o wire binário-friendly.
/// </summary>
public enum SduiUnknownFallback : byte
{
    /// Renderiza os filhos do nó como se o nó fosse um container transparente.
    /// Default do contrato — nós novos tendem a ser wrappers sobre tipos conhecidos.
    RenderChildren = 0,

    /// Desenha um placeholder visível ("nó não suportado") no lugar. Útil em dev/HML
    /// pra flagrar incompatibilidade sem esconder o problema.
    Placeholder = 1,

    /// Pula o nó E seus filhos silenciosamente (não ocupa espaço). Para conteúdo
    /// puramente opcional/decorativo de uma versão futura.
    Ignore = 2,
}

/// <summary>Versão de schema que este build do descritor emite/entende.</summary>
public static class SduiSchema
{
    /// Versão corrente do schema SDUI emitida por este assembly.
    /// v1 = Board Kanban (13 tipos). v2 = a11y, responsivo, List virtualizada,
    /// navegação e degradação graciosa (esta Onda 1).
    public const int CurrentVersion = 2;
}

/// <summary>Utilitários de reconhecimento de nó e negociação de versão.</summary>
public static class SduiCompatibility
{
    /// <summary>
    /// True se <paramref name="type"/> é um tipo de nó nomeado neste schema.
    /// Um descritor "do futuro" pode carregar valores fora desta faixa; o parse
    /// não quebra (o valor bruto é preservado), mas o host deve aplicar Fallback.
    /// </summary>
    public static bool IsKnown(this SduiNodeType type) =>
        type is >= SduiNodeType.Screen and <= SduiNodeType.NavStack;

    /// <summary>
    /// Decide como o host deve tratar <paramref name="node"/> dada a sua própria
    /// versão de schema. Retorna a política de fallback a aplicar, ou null se o
    /// nó é totalmente suportado (Type conhecido e MinSchemaVersion satisfeito).
    /// </summary>
    public static SduiUnknownFallback? ResolveFallback(this SduiNode node, int hostSchemaVersion)
    {
        var needsFallback =
            !node.Type.IsKnown() ||
            (node.MinSchemaVersion is int min && min > hostSchemaVersion);

        return needsFallback ? (node.Fallback ?? SduiUnknownFallback.RenderChildren) : null;
    }
}

/// <summary>
/// Opções JSON canônicas do transporte SDUI v1+. Contrato do wire:
///   • camelCase (bate com o decode do host Swift: schemaVersion, onTap, fontSize…);
///   • enums como NÚMERO (byte) — o host Swift decodifica UInt8; nada de string;
///   • omite null (documento enxuto e diffável).
/// Emissor (board_gen/guest) e testes compartilham ESTAS opções.
/// </summary>
public static class SduiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Sem JsonStringEnumConverter de propósito: enums vão como número.
    };

    public static string Serialize(SduiDocument document) =>
        JsonSerializer.Serialize(document, Options);

    public static SduiDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize<SduiDocument>(json, Options);
}
