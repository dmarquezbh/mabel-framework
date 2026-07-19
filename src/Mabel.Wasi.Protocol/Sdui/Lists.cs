namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Listas virtualizadas / lazy.
//
// O tipo de nó `List` (0x05) já existia como "coleção de filhos homogêneos", mas
// sem semântica de reciclagem: um host ingênuo expandiria N filhos e explodiria
// a memória/latência numa lista de milhares de cards.
//
// SduiListData dá a semântica LAZY: em vez de N nós materializados, o descritor
// carrega UM template de linha + os DADOS das linhas (uma janela deles). O host
// instancia só as views visíveis e RECICLA conforme rola (UICollectionView/
// UITableView diffable, LazyColumn, etc.).
//
// Distinção explícita:
//   • VStack + Children[]      → N nós estáticos, todos materializados.
//   • List + SduiListData      → template + dados, host recicla (virtualizado).
//
// Binding: o ItemTemplate referencia os dados de cada linha via SduiNode.Bind
// (prop-alvo → chave em SduiListItem.Data). O host substitui por linha ao reciclar.
// =============================================================================

/// <summary>
/// Uma linha de dados de uma lista virtualizada. Não é um nó — é o DADO que o
/// ItemTemplate consome. Mantém o descritor pequeno mesmo com muitas linhas.
/// </summary>
public sealed record SduiListItem
{
    /// Id semântico estável da linha (ex.: "card:50231"). Devolvido ao app no tap
    /// e usado como identidade de diffing/reciclagem pelo host.
    public required string Id { get; init; }

    /// Dados da linha. O ItemTemplate liga campos via SduiNode.Bind
    /// (ex.: Bind["text"] = "titulo" → o Text da linha vem de Data["titulo"]).
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// Ação de tap específica desta linha. Ausente ⇒ usa o OnTap do ItemTemplate
    /// (que pode referenciar Data via Args pra distinguir a linha).
    public SduiAction? OnTap { get; init; }
}

/// <summary>
/// Semântica de lista virtualizada anexada a um nó Type=List (via SduiNode.List).
/// </summary>
public sealed record SduiListData
{
    /// Template de UMA linha. Instanciado/reciclado por item; seus nós ligam-se
    /// aos dados da linha via SduiNode.Bind. É o único "nó" materializado no
    /// descritor, independentemente de quantas linhas existam.
    public required SduiNode ItemTemplate { get; init; }

    /// Janela de linhas atualmente disponível. Pode ser um subconjunto de Count
    /// (paginação/streaming); o host pede mais ao se aproximar do fim.
    public IReadOnlyList<SduiListItem>? Items { get; init; }

    /// Virtualizado: o host recicla views em vez de materializar todas. Default
    /// true — é o propósito do tipo. false ⇒ trata como coleção estática pequena.
    public bool Virtualized { get; init; } = true;

    /// Direção de rolagem/layout da lista.
    public SduiAxis? Axis { get; init; }

    /// Extensão estimada de cada item (px) no eixo principal — ajuda o host a
    /// dimensionar a scrollbar e a janela de reciclagem antes do layout real.
    public float? EstimatedItemExtent { get; init; }

    /// Total lógico de itens quando Items é apenas uma janela. Ausente ⇒ Items.Count.
    public int? Count { get; init; }

    /// Offset do primeiro item de Items dentro do total lógico (janela). Default 0.
    public int? WindowStart { get; init; }
}
