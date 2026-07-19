namespace Mabel.Wasi.Protocol.Capabilities;

// =============================================================================
// Manifesto de capabilities — a declaração capability-based do app.
//
// Princípio (POLA / least authority): o host NÃO liga nenhuma API nativa por
// padrão. Ele lê este manifesto no load e só provê o import real das
// capabilities DECLARADAS. Uma capability não declarada recebe um stub que
// responde CapStatus.NotAuthorized na hora — o guest nunca alcança o SO.
//
// PLATFORM-NEUTRAL: este manifesto é o mesmo pra iOS e Android (alvos co-iguais).
// Cada host — Swift no iOS, Kotlin/Java no Android — lê o mesmo JSON.
//
// Duas camadas de gate (ver ADR 0002 §Segurança):
//   (1) MANIFESTO (host-side, este arquivo): atenuação de autoridade. Estático,
//       auditável, versionado com o app. É o que o host confia.
//   (2) SO/RUNTIME: consentimento do usuário via prompt nativo (iOS e Android
//       runtime permissions). Mesmo declarada, câmera/GPS/notif/biometria ainda
//       precisam do "Permitir".
//
// Ponte com o build (fonte única nas duas plataformas): de CapabilityId +
// UsageDescription, cada host deriva a entrada nativa —
//   iOS:     usage-string no Info.plist (ex.: NSCameraUsageDescription) via xtool;
//   Android: <uses-permission> no AndroidManifest.xml (ex.: android.permission.CAMERA)
//            + a string de rationale do prompt runtime.
// O mapa CapabilityId → permissão nativa é do host. Evita o crash do iOS por
// usage-string ausente e o SecurityException do Android por permissão não
// declarada. Ver docs/capabilities-abi.md §5 (tabelas iOS + Android).
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
    /// Texto mostrado no prompt de permissão do SO. No iOS vira a usage-string do
    /// Info.plist (ex.: NSCameraUsageDescription); no Android vira a string de
    /// rationale exibida antes do prompt runtime. Obrigatório para as capabilities
    /// que exigem consentimento; ignorado para as que não exigem
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

    /// <summary>Id do app: bundle id no iOS (Info.plist) / applicationId no Android.</summary>
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
