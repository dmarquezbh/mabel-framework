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
//   • Retorno das funções async/subscribe = i32 = CapStatus (aceite/negação).
//   • Funções síncronas (clipboard/keychain/haptics/state) retornam direto.
//
// DOIS padrões assíncronos (ver ADR 0002 one-shot; ADR 0003 stream):
//   • ONE-SHOT: request-id = i64 gerado pelo guest. Uma chamada → UM resultado
//     no export OnCapabilityResult. Ex.: camera_capture, ble_connect, perm_request.
//   • STREAM/SUBSCRIPTION: subscription-id = i64 gerado pelo guest. Uma chamada
//     subscribe → N eventos ao longo do tempo no export OnCapabilityEvent, até o
//     guest chamar Unsubscribe(sub_id). Ex.: ble_start_scan, ble_subscribe_char,
//     location_subscribe_updates, notify_subscribe_received. request-id e
//     subscription-id são espaços de id SEPARADOS.
//
// Ownership de memória do payload (vale pros dois callbacks):
//   1. host precisa escrever o payload na memória do GUEST;
//   2. host chama o export `cap_alloc(len) -> ptr` (guest aloca buffer);
//   3. host copia os bytes e chama on_capability_result/on_capability_event(...ptr,len);
//   4. guest lê, despacha (por request-id ou subscription-id) e chama `cap_free(ptr,len)`.
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
    /// <summary>Callback ONE-SHOT de resultado async. Assinatura core:
    /// <c>(req_id:i64, cap_id:i32, status:i32, payload_ptr:i32, payload_len:i32)</c>.</summary>
    public const string OnCapabilityResult = "mabel_on_capability_result";
    /// <summary>Callback de EVENTO de STREAM (ADR 0003). Chamado N vezes por
    /// assinatura ativa. Assinatura core:
    /// <c>(sub_id:i64, cap_id:i32, event_kind:i32, payload_ptr:i32, payload_len:i32)</c>.</summary>
    public const string OnCapabilityEvent = "mabel_on_capability_event";
    /// <summary>Aloca buffer na memória linear do guest. <c>(len:i32) -> ptr:i32</c>.</summary>
    public const string Alloc = "cap_alloc";
    /// <summary>Libera buffer. <c>(ptr:i32, len:i32)</c>.</summary>
    public const string Free = "cap_free";

    // ── Streaming genérico (ADR 0003) ───────────────────────────────────────────
    /// <summary>Cancela uma assinatura. <c>(sub_id:i64)</c>. O host roteia por
    /// sub_id → capability + recurso nativo. Fire-and-forget.</summary>
    public const string Unsubscribe = "cap_unsubscribe";

    // ── Permissions ────────────────────────────────────────────────────────────
    public const string PermCheck   = "cap_perm_check";    // (cap_id:i32) -> state:i32  [sync]
    public const string PermRequest = "cap_perm_request";  // (req_id:i64, cap_id:i32) -> status:i32

    // ── Camera + Photo library ──────────────────────────────────────────────────
    public const string CameraCapture   = "cap_camera_capture";        // (req_id:i64, opts_ptr, opts_len) -> status
    public const string CameraPick       = "cap_camera_pick";           // (req_id:i64, opts_ptr, opts_len) -> status
    public const string CameraReadAsset  = "cap_camera_read_asset";     // (id_ptr,id_len, off:i64, len:i32, out_ptr) -> read:i32 [sync]
    public const string CameraReleaseAsset = "cap_camera_release_asset"; // (id_ptr, id_len) [sync]

    // ── Location (one-shot + STREAM) ──────────────────────────────────────────────
    public const string LocationGetCurrent      = "cap_location_get_current";     // (req_id:i64, accuracy:i32) -> status  [one-shot]
    public const string LocationSubscribeUpdates = "cap_location_subscribe_updates"; // (sub_id:i64, accuracy:i32) -> status  [STREAM; pare via Unsubscribe]

    // ── Notifications (local; agenda one-shot + STREAM de recebidas) ──────────────
    public const string NotifySchedule        = "cap_notify_schedule";         // (req_id:i64, json_ptr, json_len) -> status
    public const string NotifyCancel           = "cap_notify_cancel";           // (id_ptr, id_len)
    public const string NotifyCancelAll        = "cap_notify_cancel_all";       // ()
    public const string NotifySubscribeReceived = "cap_notify_subscribe_received"; // (sub_id:i64) -> status  [STREAM]

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

    // ── Bluetooth (BLE central) — one-shot + STREAM (ADR 0003) ────────────────────
    public const string BleState             = "cap_ble_state";              // () -> adapter_state:i32 [sync]
    public const string BleStartScan          = "cap_ble_start_scan";         // (sub_id:i64, filter_ptr, filter_len) -> status  [STREAM ev0=device-found]
    public const string BleConnect            = "cap_ble_connect";            // (req_id:i64, pid_ptr, pid_len) -> status  [one-shot]
    public const string BleDisconnect         = "cap_ble_disconnect";         // (pid_ptr, pid_len)
    public const string BleDiscover           = "cap_ble_discover";           // (req_id:i64, pid_ptr, pid_len) -> status  [one-shot, payload=gatt]
    public const string BleReadCharacteristic  = "cap_ble_read_char";          // (req_id:i64, pid_ptr,pid_len, uuid_ptr,uuid_len) -> status  [one-shot]
    public const string BleWriteCharacteristic = "cap_ble_write_char";         // (req_id:i64, pid_ptr,pid_len, uuid_ptr,uuid_len, val_ptr,val_len, with_resp:i32) -> status
    public const string BleSubscribeCharacteristic = "cap_ble_subscribe_char"; // (sub_id:i64, pid_ptr,pid_len, uuid_ptr,uuid_len) -> status  [STREAM ev1=char-changed]
    public const string BleSubscribeConnection = "cap_ble_subscribe_connection"; // (sub_id:i64, pid_ptr,pid_len) -> status  [STREAM ev2=connection-changed]
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
    Bluetooth     = 9,
}

/// <summary>
/// Discriminador do <c>event-kind</c> nos eventos de STREAM do bluetooth
/// (payload do <see cref="CapabilityContract.OnCapabilityEvent"/> quando
/// <c>capability = Bluetooth</c>). Casa com os event-kind documentados em
/// wit/bluetooth.wit. Cada capability com stream tem seu próprio conjunto.
/// </summary>
public enum BleEventKind : uint
{
    /// <summary>Scan achou um peripheral. Payload = advertisement (JSON).</summary>
    DeviceFound          = 0,
    /// <summary>Característica notificou/indicou. Payload = bytes novos.</summary>
    CharacteristicChanged = 1,
    /// <summary>Conexão mudou. Payload = 1 byte (1=conectado, 0=desconectado).</summary>
    ConnectionChanged    = 2,
}
