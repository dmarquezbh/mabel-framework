import Foundation

// =============================================================================
// Payloads tipados das capabilities (lado host). Codable ⇄ JSON — o formato do
// wire p/ params/resultados que não cabem em i32/i64. Espelham os records do WIT.
//
// PLATFORM-AGNOSTIC (Foundation apenas). As impls nativas iOS usam estes tipos.
// =============================================================================

// ── Camera / photo ────────────────────────────────────────────────────────────

public enum MediaKind: String, Codable { case photo, video }
public enum CameraFacing: String, Codable { case front, back }

public struct CaptureOptions: Codable {
    public var kind: MediaKind
    public var facing: CameraFacing
    public var quality: Float
    public var allowEdit: Bool
    public init(kind: MediaKind, facing: CameraFacing, quality: Float, allowEdit: Bool) {
        self.kind = kind; self.facing = facing; self.quality = quality; self.allowEdit = allowEdit
    }
}

public struct PickerOptions: Codable {
    public var kind: MediaKind
    public var maxItems: UInt32
    public init(kind: MediaKind, maxItems: UInt32) {
        self.kind = kind; self.maxItems = maxItems
    }
}

/// Metadados + handle do asset capturado/selecionado (bytes lidos via read-asset).
public struct CapturedAsset: Codable {
    public var assetId: String
    public var kind: MediaKind
    public var width: UInt32
    public var height: UInt32
    public var byteSize: UInt64
    public var mime: String
}

// ── Location ────────────────────────────────────────────────────────────────────

public enum LocationAccuracy: String, Codable { case coarse, balanced, precise }

public struct Position: Codable {
    public var latitude: Double
    public var longitude: Double
    public var accuracyM: Double
    public var altitudeM: Double?
    public var headingDeg: Double?
    public var speedMps: Double?
    public var timestampMs: UInt64
}

// ── Notifications ─────────────────────────────────────────────────────────────

/// Gatilho: afterSeconds OU atTime (epoch ms). Só um é setado.
public struct NotificationTrigger: Codable {
    public var afterSeconds: UInt32?
    public var atTimeMs: UInt64?
    public init(afterSeconds: UInt32?, atTimeMs: UInt64?) {
        self.afterSeconds = afterSeconds; self.atTimeMs = atTimeMs
    }
}

public struct LocalNotification: Codable {
    public var id: String
    public var title: String
    public var body: String
    public var sound: String?
    public var badge: UInt32?
    public var trigger: NotificationTrigger
    public init(id: String, title: String, body: String, sound: String?, badge: UInt32?, trigger: NotificationTrigger) {
        self.id = id; self.title = title; self.body = body
        self.sound = sound; self.badge = badge; self.trigger = trigger
    }
}

// ── Biometrics ────────────────────────────────────────────────────────────────

public enum BiometryKind: Int32 { case none = 0, touchID = 1, faceID = 2, opticID = 3 }
public enum BiometryPolicy: String, Codable { case biometricsOnly, biometricsOrPasscode }

// ── Secure storage ────────────────────────────────────────────────────────────

public enum SecureAccessibility: String, Codable {
    case whenUnlocked
    case afterFirstUnlock
    case whenPasscodeSetThisDeviceOnly
}

public struct SecurePutOptions: Codable {
    public var accessibility: SecureAccessibility
    public var requireUserPresence: Bool
    public init(accessibility: SecureAccessibility, requireUserPresence: Bool) {
        self.accessibility = accessibility; self.requireUserPresence = requireUserPresence
    }
}

// ── Share ───────────────────────────────────────────────────────────────────────

/// Um item de share. Exatamente um campo é não-nulo (variant achatado).
public struct ShareItem: Codable {
    public var text: String?
    public var url: String?
    public var assetId: String?
    public var blob: ShareBlob?
    public init(text: String? = nil, url: String? = nil, assetId: String? = nil, blob: ShareBlob? = nil) {
        self.text = text; self.url = url; self.assetId = assetId; self.blob = blob
    }
}

public struct ShareBlob: Codable {
    public var mime: String
    /// Base64 no JSON (bytes inline pequenos).
    public var bytesBase64: String
    public var filename: String?
}

// ── Bluetooth (BLE) ───────────────────────────────────────────────────────────

public struct BleScanFilter: Codable {
    public var serviceUuids: [String]
    public var allowDuplicates: Bool
    public init(serviceUuids: [String], allowDuplicates: Bool) {
        self.serviceUuids = serviceUuids; self.allowDuplicates = allowDuplicates
    }
}

public struct BleAdvertisement: Codable {
    public var peripheralId: String
    public var name: String?
    public var rssi: Int32
    public var serviceUuids: [String]
    /// Base64 se presente.
    public var manufacturerDataBase64: String?
}

public struct BleCharacteristic: Codable {
    public var uuid: String
    /// Flags OR-áveis: 1=read,2=write,4=writeNoResp,8=notify,16=indicate.
    public var properties: UInt32
}

public struct BleService: Codable {
    public var uuid: String
    public var characteristics: [BleCharacteristic]
}

public struct BleGatt: Codable {
    public var peripheralId: String
    public var services: [BleService]
}

// MARK: - JSON helpers

public enum CapabilityJSON {
    public static let encoder = JSONEncoder()
    public static let decoder = JSONDecoder()

    public static func encode<T: Encodable>(_ value: T) -> Data? {
        try? encoder.encode(value)
    }
    public static func decode<T: Decodable>(_ type: T.Type, from data: Data) -> T? {
        try? decoder.decode(type, from: data)
    }
}
