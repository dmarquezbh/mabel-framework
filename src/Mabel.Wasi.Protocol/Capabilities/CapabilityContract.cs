namespace Mabel.Wasi.Protocol.Capabilities;

// =============================================================================
// Lowering ACHATADO da ABI de capabilities para core-module WASI Preview 1.
//
// Irmão de WasiContract.cs: enquanto os .wit em Capabilities/wit/ descrevem o
// contrato SEMÂNTICO (north star, Component Model), este arquivo fixa os NOMES
// DE FUNÇÃO reais que o guest importa/exporta HOJE — porque o stack atual
// (guest = .NET → WASI p1 core module; host = WasmKit/Swift) não tem Component
// Model nem async/futures de p2 sólidos. Ver ADR 0002.
//
// Convenções do wire (todas as funções são core WASM, params i32/i64/f32/f64):
//   • Strings e records passam como (ptr:i32, len:i32) em memória linear,
//     serializados em UTF-8 / JSON (mesmo estilo do draw_text já existente).
//   • request-id = i64. Cada chamada async o gera e casa com o callback.
//   • Retorno das funções async = i32 = CapStatus (aceite imediato/negação).
//     O RESULTADO real chega depois no export OnCapabilityResult.
//   • Funções síncronas (clipboard/keychain/haptics) retornam direto.
//
// Ownership de memória do resultado assíncrono (callback):
//   1. host precisa escrever o payload na memória do GUEST;
//   2. host chama o export `cap_alloc(len) -> ptr` (guest aloca buffer);
//   3. host copia os bytes e chama `mabel_on_capability_result(...ptr,len)`;
//   4. guest lê, despacha por (request-id, capability) e chama `cap_free(ptr,len)`.
// =============================================================================

/// <summary>
/// Nomes de módulo e função da ABI de capabilities no wire core-module.
/// Espelha o papel de <c>WasiContract</c> para o canal de render.
/// </summary>
public static class CapabilityContract
{
    /// <summary>Módulo de import das capabilities (separado do "mabel" de render).</summary>
    public const string HostModule = "mabel:cap";

    // ── Guest exports (o host chama) ──────────────────────────────────────────
    /// <summary>Callback único de resultado assíncrono. Assinatura core:
    /// <c>(req_id:i64, cap_id:i32, status:i32, payload_ptr:i32, payload_len:i32)</c>.</summary>
    public const string OnCapabilityResult = "mabel_on_capability_result";
    /// <summary>Aloca buffer na memória linear do guest. <c>(len:i32) -> ptr:i32</c>.</summary>
    public const string Alloc = "cap_alloc";
    /// <summary>Libera buffer. <c>(ptr:i32, len:i32)</c>.</summary>
    public const string Free = "cap_free";

    // ── Permissions ────────────────────────────────────────────────────────────
    public const string PermCheck   = "cap_perm_check";    // (cap_id:i32) -> state:i32  [sync]
    public const string PermRequest = "cap_perm_request";  // (req_id:i64, cap_id:i32) -> status:i32

    // ── Camera + Photo library ──────────────────────────────────────────────────
    public const string CameraCapture   = "cap_camera_capture";        // (req_id:i64, opts_ptr, opts_len) -> status
    public const string CameraPick       = "cap_camera_pick";           // (req_id:i64, opts_ptr, opts_len) -> status
    public const string CameraReadAsset  = "cap_camera_read_asset";     // (id_ptr,id_len, off:i64, len:i32, out_ptr) -> read:i32 [sync]
    public const string CameraReleaseAsset = "cap_camera_release_asset"; // (id_ptr, id_len) [sync]

    // ── Location ────────────────────────────────────────────────────────────────
    public const string LocationGetCurrent  = "cap_location_get_current";  // (req_id:i64, accuracy:i32) -> status
    public const string LocationStartUpdates = "cap_location_start_updates"; // (req_id:i64, accuracy:i32) -> status
    public const string LocationStopUpdates  = "cap_location_stop_updates";  // (req_id:i64)

    // ── Notifications (local) ────────────────────────────────────────────────────
    public const string NotifySchedule  = "cap_notify_schedule";   // (req_id:i64, json_ptr, json_len) -> status
    public const string NotifyCancel     = "cap_notify_cancel";     // (id_ptr, id_len)
    public const string NotifyCancelAll  = "cap_notify_cancel_all"; // ()

    // ── Biometrics ──────────────────────────────────────────────────────────────
    public const string BiometricsAvailable    = "cap_biometrics_available";    // () -> kind:i32 [sync]
    public const string BiometricsAuthenticate = "cap_biometrics_authenticate"; // (req_id:i64, reason_ptr, reason_len, policy:i32) -> status

    // ── Secure storage (Keychain) — tudo síncrono ────────────────────────────────
    public const string SecurePut    = "cap_secure_put";    // (key_ptr,key_len, val_ptr,val_len, access:i32, presence:i32) -> status
    public const string SecureGet    = "cap_secure_get";    // (key_ptr,key_len, out_ptr) -> status (out via cap_alloc)
    public const string SecureDelete = "cap_secure_delete"; // (key_ptr, key_len) -> status
    public const string SecureKeys   = "cap_secure_keys";   // (out_ptr) -> status (JSON array via cap_alloc)

    // ── Share ─────────────────────────────────────────────────────────────────────
    public const string SharePresent = "cap_share_present"; // (req_id:i64, json_ptr, json_len) -> status

    // ── Clipboard — síncrono ────────────────────────────────────────────────────────
    public const string ClipboardWriteText = "cap_clipboard_write_text"; // (txt_ptr, txt_len) -> status
    public const string ClipboardReadText  = "cap_clipboard_read_text";  // (out_ptr) -> len:i32 (-1 = none)
    public const string ClipboardHasText   = "cap_clipboard_has_text";   // () -> bool:i32

    // ── Haptics — fire-and-forget, síncrono ──────────────────────────────────────────
    public const string HapticsImpact       = "cap_haptics_impact";       // (style:i32)
    public const string HapticsNotification = "cap_haptics_notification"; // (kind:i32)
    public const string HapticsSelection    = "cap_haptics_selection";    // ()
}

/// <summary>
/// Resultado de uma operação de capability. Valores byte-estáveis (parte da ABI;
/// devem casar com o enum <c>cap-status</c> em wit/types.wit).
/// </summary>
public enum CapStatus : byte
{
    Ok               = 0,
    PermissionDenied = 1,
    /// <summary>Capability não declarada no manifesto — host recusa sem tocar o SO.</summary>
    NotAuthorized    = 2,
    Unavailable      = 3,
    Cancelled        = 4,
    Timeout          = 5,
    Error            = 6,
}

/// <summary>Estado de autorização do SO. Casa com <c>permission-state</c> no WIT.</summary>
public enum PermissionState : byte
{
    NotDetermined = 0,
    Granted       = 1,
    Denied        = 2,
    Restricted    = 3,
}

/// <summary>
/// Identificador estável de capability para roteamento do callback achatado e
/// para o manifesto. Casa com <c>capability-id</c> no WIT.
/// </summary>
public enum CapabilityId : byte
{
    Camera        = 0,
    PhotoLibrary  = 1,
    Location      = 2,
    Notifications = 3,
    Biometrics    = 4,
    SecureStorage = 5,
    Share         = 6,
    Clipboard     = 7,
    Haptics       = 8,
}
