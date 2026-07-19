import Foundation

// =============================================================================
// Mabel Capabilities — contrato do wire (lado Swift/host).
//
// Espelha src/Mabel.Wasi.Protocol/Capabilities/CapabilityContract.cs. Estes são
// os nomes e enums da ABI achatada (core-module WASI p1): o guest importa as
// funções `cap_*`, o host exporta os callbacks `mabel_on_capability_*`.
//
// Este arquivo é PLATFORM-AGNOSTIC (Foundation apenas) — compila em Linux, o que
// permite typecheck do core sem o Darwin SDK. As implementações NATIVAS (UIKit,
// AVFoundation, CoreBluetooth…) vivem em arquivos separados iOS-only.
// =============================================================================

/// Resultado de uma operação. Valores estáveis (parte da ABI; casam com
/// `cap-status` no WIT e `CapStatus` no C#). Int32 = o tipo do wire.
public enum CapStatus: Int32, Error {
    case ok = 0
    case permissionDenied = 1
    /// Capability não declarada no manifesto — host recusa sem tocar o SO.
    case notAuthorized = 2
    case unavailable = 3
    case cancelled = 4
    case timeout = 5
    case error = 6
}

/// Estado de autorização do SO. Casa com `permission-state` / `PermissionState`.
public enum PermissionState: Int32 {
    case notDetermined = 0
    case granted = 1
    case denied = 2
    case restricted = 3
}

/// Identificador estável de capability. Casa com `capability-id` / `CapabilityId`.
public enum CapabilityId: Int32, CaseIterable {
    case camera = 0
    case photoLibrary = 1
    case location = 2
    case notifications = 3
    case biometrics = 4
    case secureStorage = 5
    case share = 6
    case clipboard = 7
    case haptics = 8
    case bluetooth = 9

    /// Nome kebab-case usado no manifesto JSON e no WIT.
    public var manifestKey: String {
        switch self {
        case .camera: return "camera"
        case .photoLibrary: return "photo-library"
        case .location: return "location"
        case .notifications: return "notifications"
        case .biometrics: return "biometrics"
        case .secureStorage: return "secure-storage"
        case .share: return "share"
        case .clipboard: return "clipboard"
        case .haptics: return "haptics"
        case .bluetooth: return "bluetooth"
        }
    }

    public init?(manifestKey: String) {
        guard let match = CapabilityId.allCases.first(where: { $0.manifestKey == manifestKey })
        else { return nil }
        self = match
    }
}

/// event-kind dos eventos de STREAM do bluetooth. Casa com `BleEventKind` no C#.
public enum BleEventKind: UInt32 {
    case deviceFound = 0
    case characteristicChanged = 1
    case connectionChanged = 2
}

/// Nomes de módulo/função do wire core-module. Fonte única no lado Swift.
public enum CapabilityWireNames {
    public static let hostModule = "mabel:cap"

    // Guest exports (o host chama)
    public static let onCapabilityResult = "mabel_on_capability_result"
    public static let onCapabilityEvent = "mabel_on_capability_event"
    public static let alloc = "cap_alloc"
    public static let free = "cap_free"

    // Streaming genérico
    public static let unsubscribe = "cap_unsubscribe"

    // Permissions
    public static let permCheck = "cap_perm_check"
    public static let permRequest = "cap_perm_request"

    // Camera + Photo library
    public static let cameraCapture = "cap_camera_capture"
    public static let cameraPick = "cap_camera_pick"
    public static let cameraReadAsset = "cap_camera_read_asset"
    public static let cameraReleaseAsset = "cap_camera_release_asset"

    // Location
    public static let locationGetCurrent = "cap_location_get_current"
    public static let locationSubscribeUpdates = "cap_location_subscribe_updates"

    // Notifications
    public static let notifySchedule = "cap_notify_schedule"
    public static let notifyCancel = "cap_notify_cancel"
    public static let notifyCancelAll = "cap_notify_cancel_all"
    public static let notifySubscribeReceived = "cap_notify_subscribe_received"

    // Biometrics
    public static let biometricsAvailable = "cap_biometrics_available"
    public static let biometricsAuthenticate = "cap_biometrics_authenticate"

    // Secure storage
    public static let securePut = "cap_secure_put"
    public static let secureGet = "cap_secure_get"
    public static let secureDelete = "cap_secure_delete"
    public static let secureKeys = "cap_secure_keys"

    // Share
    public static let sharePresent = "cap_share_present"

    // Clipboard
    public static let clipboardWriteText = "cap_clipboard_write_text"
    public static let clipboardReadText = "cap_clipboard_read_text"
    public static let clipboardHasText = "cap_clipboard_has_text"

    // Haptics
    public static let hapticsImpact = "cap_haptics_impact"
    public static let hapticsNotification = "cap_haptics_notification"
    public static let hapticsSelection = "cap_haptics_selection"

    // Bluetooth
    public static let bleState = "cap_ble_state"
    public static let bleStartScan = "cap_ble_start_scan"
    public static let bleConnect = "cap_ble_connect"
    public static let bleDisconnect = "cap_ble_disconnect"
    public static let bleDiscover = "cap_ble_discover"
    public static let bleReadCharacteristic = "cap_ble_read_char"
    public static let bleWriteCharacteristic = "cap_ble_write_char"
    public static let bleSubscribeCharacteristic = "cap_ble_subscribe_char"
    public static let bleSubscribeConnection = "cap_ble_subscribe_connection"
}
