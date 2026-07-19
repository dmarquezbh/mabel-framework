namespace Mabel.Wasi.Protocol.Capabilities;

// =============================================================================
// Manifesto de capabilities — a declaração capability-based do app.
//
// Princípio (POLA / least authority): o host NÃO liga nenhuma API nativa por
// padrão. Ele lê este manifesto no load e só provê o import real das
// capabilities DECLARADAS. Uma capability não declarada recebe um stub que
// responde CapStatus.NotAuthorized na hora — o guest nunca alcança o SO.
//
// Duas camadas de gate (ver ADR 0002 §Segurança):
//   (1) MANIFESTO (host-side, este arquivo): atenuação de autoridade. Estático,
//       auditável, versionado com o app. É o que o host confia.
//   (2) SO/RUNTIME (iOS): consentimento do usuário via prompt nativo. Mesmo
//       declarada, câmera/GPS/notif/biometria ainda precisam do "Permitir".
//
// Ponte com o build (xtool): cada entry carrega a(s) usage-string(s) de
// Info.plist que aquela capability exige. O passo de build (xtool.yml/Info.plist)
// injeta essas chaves — o manifesto é a fonte única, evitando prompt do SO que
// crasha o app por falta de usage-string. Ver docs/capabilities-abi.md §iOS.
//
// Transporte v1 = JSON (mesmo estilo do SduiDocument). Fica ao lado do app WASM
// no bundle (ex.: "mabel.caps.json").
// =============================================================================

/// <summary>
/// Declaração de UMA capability que o app quer usar, com a justificativa que o
/// SO mostra ao usuário e os metadados de build.
/// </summary>
public sealed record CapabilityGrant
{
    /// <summary>Qual capability. O host liga o import correspondente.</summary>
    public required CapabilityId Capability { get; init; }

    /// <summary>
    /// Texto mostrado no prompt de permissão do SO (vira a usage-string do
    /// Info.plist no iOS — ex.: NSCameraUsageDescription). Obrigatório para as
    /// capabilities que exigem consentimento; ignorado para as que não exigem
    /// (share/clipboard/haptics).
    /// </summary>
    public string? UsageDescription { get; init; }

    /// <summary>
    /// Flags opcionais por capability (ex.: location "precise" vs "coarse",
    /// notifications "sound"/"badge"). Bag achatado; só o relevante é setado.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }
}

/// <summary>
/// Manifesto do app. É isto que trafega como JSON e o host carrega no load.
/// </summary>
public sealed record CapabilityManifest
{
    /// <summary>Versão do schema — host recusa/adapta se não reconhecer.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Bundle id do app (casa com o Info.plist gerado pelo xtool).</summary>
    public required string AppId { get; init; }

    /// <summary>
    /// Capabilities concedidas. O que NÃO estiver aqui é negado por construção
    /// (NotAuthorized). Lista vazia = app puramente SDUI, zero acesso nativo.
    /// </summary>
    public IReadOnlyList<CapabilityGrant> Grants { get; init; } = [];

    /// <summary>True se a capability foi declarada (helper de atenuação do host).</summary>
    public bool IsGranted(CapabilityId capability)
    {
        foreach (var g in Grants)
            if (g.Capability == capability) return true;
        return false;
    }
}
