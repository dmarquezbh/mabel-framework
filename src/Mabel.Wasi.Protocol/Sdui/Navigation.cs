namespace Mabel.Wasi.Protocol.Sdui;

// =============================================================================
// Navegação / routing declarativo.
//
// Uma tela isolada não é um app. O descritor precisa expressar uma PILHA de
// telas e transições entre elas — sem que o guest manipule imperativamente a
// UINavigationController.
//
// Dois pedaços:
//   • Estrutura: o nó NavStack (0x0E) hospeda Screens; cada Screen carrega
//     SduiNav (rota nomeada + título).
//   • Ações: SduiAction.Navigate declara a transição (push/pop/replace/root/
//     popTo) com rota-alvo + params (deep-link). O host executa na pilha nativa.
//
// Named routes + params = deep-linking: uma URL externa vira {route, params} e
// o host reconstrói a pilha.
// =============================================================================

/// <summary>
/// Tipo de transição de navegação. Byte-enum (decode UInt8 no host Swift).
/// </summary>
public enum SduiNavKind : byte
{
    /// Empilha uma nova tela (Route) sobre a atual.
    Push = 0,
    /// Desempilha a tela atual (volta uma).
    Pop = 1,
    /// Substitui a tela atual pela de Route (sem crescer a pilha).
    Replace = 2,
    /// Reseta a pilha, deixando só a raiz (ou Route como nova raiz).
    Root = 3,
    /// Desempilha até a tela cujo Route casa (pop-to-route).
    PopTo = 4,
}

/// <summary>
/// Transição de navegação declarativa carregada por uma SduiAction. O host a
/// aplica à pilha do NavStack ancestral.
/// </summary>
public sealed record SduiNavigate
{
    /// Tipo de transição.
    public required SduiNavKind Kind { get; init; }

    /// Rota nomeada alvo (ex.: "card/50231"). Obrigatória em Push/Replace/PopTo;
    /// opcional em Root (null ⇒ raiz existente); ignorada em Pop.
    public string? Route { get; init; }

    /// Parâmetros da rota (deep-link args). Entregues ao Screen destino.
    public IReadOnlyDictionary<string, string>? Params { get; init; }
}

/// <summary>
/// Metadados de navegação de um Screen dentro de um NavStack.
/// </summary>
public sealed record SduiNav
{
    /// Rota nomeada que identifica este Screen (alvo de Push/Replace/PopTo e de
    /// deep-links). Ex.: "board", "card/:id".
    public string? Route { get; init; }

    /// Título exibido na navigation bar.
    public string? Title { get; init; }

    /// Apresentado modalmente (sheet/full-screen) em vez de empilhado.
    public bool? Modal { get; init; }

    /// Oculta a navigation bar neste Screen.
    public bool? HidesNavBar { get; init; }
}
