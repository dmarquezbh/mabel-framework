import Foundation

// =============================================================================
// Wire da ABI de capabilities (lado host) — PLATFORM-AGNOSTIC (Foundation).
//
// Três peças:
//   • GuestBridge — a fronteira com o runtime WASM. Ler/escrever memória linear
//     do guest, alocar (cap_alloc) e invocar os exports de callback. UMA impl
//     concreta por runtime (WasmKit no iOS; um fake in-process no harness).
//   • CapabilityHost — o roteador do wire: aplica o gate do manifesto, delega às
//     impls nativas (via protocolos abaixo) e devolve resultados/eventos pelo
//     GuestBridge. É aqui que mora TODA a lógica de ABI; as impls só falam SO.
//   • Protocolos *Providing — o contrato que cada impl nativa (iOS) satisfaz.
//
// O adapter de runtime (futuro, WasmKit) decodifica os args i32/i64/(ptr,len) e
// chama os métodos `CapabilityHost` abaixo; o harness chama os MESMOS métodos
// direto (prova a impl nativa sem um guest .wasm). Ver docs guest-bindings.
// =============================================================================

/// A fronteira com o runtime WASM. O host escreve resultados/eventos aqui; a
/// impl concreta os empurra pro guest.
public protocol GuestBridge: AnyObject {
    /// Aloca `length` bytes na memória linear do guest (chama o export cap_alloc).
    /// Retorna o ponteiro (offset) ou 0 se não coube.
    func allocate(_ length: Int) -> UInt32
    /// Copia `bytes` pra memória do guest em `pointer`.
    func write(_ bytes: [UInt8], to pointer: UInt32)
    /// Lê `length` bytes da memória do guest a partir de `pointer`.
    func read(pointer: UInt32, length: UInt32) -> [UInt8]
    /// Invoca o export one-shot do guest (mabel_on_capability_result).
    func invokeResult(requestId: UInt64, capability: Int32, status: Int32,
                      payloadPtr: UInt32, payloadLen: UInt32)
    /// Invoca o export de stream do guest (mabel_on_capability_event).
    func invokeEvent(subscriptionId: UInt64, capability: Int32, eventKind: UInt32,
                     payloadPtr: UInt32, payloadLen: UInt32)
}

/// O que as impls de capability usam pra devolver resultados assíncronos.
public protocol CapabilityResponder: AnyObject {
    /// Resultado ONE-SHOT (uma chamada → um resultado).
    func respond(_ requestId: UInt64, _ capability: CapabilityId, _ status: CapStatus, payload: Data?)
    /// Evento de STREAM (uma assinatura → N eventos).
    func emit(_ subscriptionId: UInt64, _ capability: CapabilityId, _ eventKind: UInt32, payload: Data?)
}

// MARK: - Protocolos das impls nativas

/// Base de toda capability. Defaults cobrem caps sem permissão/stream.
public protocol CapabilityProviding: AnyObject {
    var capabilityId: CapabilityId { get }
    /// Estado de autorização do SO (síncrono). Default = concedido (caps que não
    /// pedem permissão: share/clipboard/haptics).
    func permissionState() -> PermissionState
    /// Dispara o prompt de permissão do SO (async). Default = responde granted.
    func requestPermission(_ responder: CapabilityResponder, requestId: UInt64)
    /// Cancela uma assinatura de stream. Default = no-op (caps sem stream).
    func cancelSubscription(_ subscriptionId: UInt64)
}

public extension CapabilityProviding {
    func permissionState() -> PermissionState { .granted }
    func requestPermission(_ responder: CapabilityResponder, requestId: UInt64) {
        responder.respond(requestId, capabilityId, .ok,
                          payload: Data([UInt8(PermissionState.granted.rawValue)]))
    }
    func cancelSubscription(_ subscriptionId: UInt64) {}
}

public protocol CameraProviding: CapabilityProviding {
    func capture(_ responder: CapabilityResponder, requestId: UInt64, options: CaptureOptions)
    func pick(_ responder: CapabilityResponder, requestId: UInt64, options: PickerOptions)
    func readAsset(assetId: String, offset: UInt64, length: UInt32) -> Data?
    func releaseAsset(assetId: String)
}

public protocol LocationProviding: CapabilityProviding {
    func getCurrent(_ responder: CapabilityResponder, requestId: UInt64, accuracy: LocationAccuracy)
    func subscribeUpdates(_ responder: CapabilityResponder, subscriptionId: UInt64, accuracy: LocationAccuracy)
}

public protocol NotificationsProviding: CapabilityProviding {
    func schedule(_ responder: CapabilityResponder, requestId: UInt64, notification: LocalNotification)
    func cancel(id: String)
    func cancelAll()
    func subscribeReceived(_ responder: CapabilityResponder, subscriptionId: UInt64)
}

public protocol BiometricsProviding: CapabilityProviding {
    func available() -> BiometryKind
    func authenticate(_ responder: CapabilityResponder, requestId: UInt64, reason: String, policy: BiometryPolicy)
}

public protocol SecureStorageProviding: CapabilityProviding {
    func put(key: String, value: Data, options: SecurePutOptions) -> CapStatus
    func get(key: String) -> Result<Data, CapStatus>
    func delete(key: String) -> CapStatus
    func keys() -> [String]
}

public protocol ShareProviding: CapabilityProviding {
    func present(_ responder: CapabilityResponder, requestId: UInt64, items: [ShareItem])
}

public protocol ClipboardProviding: CapabilityProviding {
    func writeText(_ text: String) -> CapStatus
    func readText() -> String?
    func hasText() -> Bool
}

public protocol HapticsProviding: CapabilityProviding {
    func impact(style: Int32)
    func notification(kind: Int32)
    func selection()
}

public protocol BluetoothProviding: CapabilityProviding {
    func state() -> Int32
    func startScan(_ responder: CapabilityResponder, subscriptionId: UInt64, filter: BleScanFilter) -> CapStatus
    func connect(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String)
    func disconnect(peripheralId: String)
    func discover(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String)
    func readCharacteristic(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String, characteristic: String)
    func writeCharacteristic(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String, characteristic: String, value: Data, withResponse: Bool)
    func subscribeCharacteristic(_ responder: CapabilityResponder, subscriptionId: UInt64, peripheralId: String, characteristic: String) -> CapStatus
    func subscribeConnection(_ responder: CapabilityResponder, subscriptionId: UInt64, peripheralId: String) -> CapStatus
}

// MARK: - CapabilityHost

/// Roteador do wire. Recebe as chamadas achatadas da ABI, aplica o gate do
/// manifesto e delega às impls nativas. É `CapabilityResponder` — as impls
/// devolvem resultados por aqui, que vão ao guest pelo GuestBridge.
public final class CapabilityHost: CapabilityResponder {
    public let manifest: CapabilityManifest
    private let bridge: GuestBridge

    // Impls nativas (setadas no registry). Ausência = capability unavailable.
    public var camera: CameraProviding?
    public var photoLibrary: CameraProviding?   // mesma impl que camera, id distinto
    public var location: LocationProviding?
    public var notifications: NotificationsProviding?
    public var biometrics: BiometricsProviding?
    public var secureStorage: SecureStorageProviding?
    public var share: ShareProviding?
    public var clipboard: ClipboardProviding?
    public var haptics: HapticsProviding?
    public var bluetooth: BluetoothProviding?

    /// subId → capability que a possui (pra rotear o unsubscribe genérico).
    private var subscriptions: [UInt64: CapabilityId] = [:]
    private let lock = NSLock()

    public init(manifest: CapabilityManifest, bridge: GuestBridge) {
        self.manifest = manifest
        self.bridge = bridge
    }

    private func provider(for cap: CapabilityId) -> CapabilityProviding? {
        switch cap {
        case .camera: return camera
        case .photoLibrary: return photoLibrary
        case .location: return location
        case .notifications: return notifications
        case .biometrics: return biometrics
        case .secureStorage: return secureStorage
        case .share: return share
        case .clipboard: return clipboard
        case .haptics: return haptics
        case .bluetooth: return bluetooth
        }
    }

    private func trackSubscription(_ subId: UInt64, _ cap: CapabilityId) {
        lock.lock(); subscriptions[subId] = cap; lock.unlock()
    }

    // MARK: CapabilityResponder

    public func respond(_ requestId: UInt64, _ capability: CapabilityId, _ status: CapStatus, payload: Data?) {
        let (ptr, len) = writePayload(payload)
        bridge.invokeResult(requestId: requestId, capability: capability.rawValue,
                            status: status.rawValue, payloadPtr: ptr, payloadLen: len)
    }

    public func emit(_ subscriptionId: UInt64, _ capability: CapabilityId, _ eventKind: UInt32, payload: Data?) {
        let (ptr, len) = writePayload(payload)
        bridge.invokeEvent(subscriptionId: subscriptionId, capability: capability.rawValue,
                           eventKind: eventKind, payloadPtr: ptr, payloadLen: len)
    }

    private func writePayload(_ data: Data?) -> (UInt32, UInt32) {
        guard let data, !data.isEmpty else { return (0, 0) }
        let ptr = bridge.allocate(data.count)
        guard ptr != 0 else { return (0, 0) }
        bridge.write([UInt8](data), to: ptr)
        return (ptr, UInt32(data.count))
    }

    // MARK: Gate

    /// True se a capability está declarada. Não declarada = não gated aqui,
    /// quem chama devolve `.notAuthorized`.
    public func isAuthorized(_ cap: CapabilityId) -> Bool { manifest.isGranted(cap) }

    // MARK: - Entradas da ABI (chamadas pelo adapter de runtime OU pelo harness)
    // Funções async/subscribe retornam CapStatus = aceite/negação imediata; o
    // resultado real vem depois via respond/emit. Funções síncronas retornam já.

    // ── Permissions ──────────────────────────────────────────────────────────
    public func permCheck(_ cap: CapabilityId) -> PermissionState {
        guard manifest.isGranted(cap), let p = provider(for: cap) else { return .denied }
        return p.permissionState()
    }

    public func permRequest(requestId: UInt64, cap: CapabilityId) -> CapStatus {
        guard manifest.isGranted(cap) else { return .notAuthorized }
        guard let p = provider(for: cap) else { return .unavailable }
        p.requestPermission(self, requestId: requestId)
        return .ok
    }

    // ── Camera / photo ─────────────────────────────────────────────────────────
    public func cameraCapture(requestId: UInt64, optionsJSON: Data) -> CapStatus {
        guard manifest.isGranted(.camera) else { return .notAuthorized }
        guard let camera else { return .unavailable }
        guard let opts = CapabilityJSON.decode(CaptureOptions.self, from: optionsJSON) else { return .error }
        camera.capture(self, requestId: requestId, options: opts)
        return .ok
    }

    public func cameraPick(requestId: UInt64, optionsJSON: Data) -> CapStatus {
        guard manifest.isGranted(.photoLibrary) else { return .notAuthorized }
        guard let photoLibrary else { return .unavailable }
        guard let opts = CapabilityJSON.decode(PickerOptions.self, from: optionsJSON) else { return .error }
        photoLibrary.pick(self, requestId: requestId, options: opts)
        return .ok
    }

    public func cameraReadAsset(assetId: String, offset: UInt64, length: UInt32) -> Data? {
        (camera ?? photoLibrary)?.readAsset(assetId: assetId, offset: offset, length: length)
    }

    public func cameraReleaseAsset(assetId: String) {
        (camera ?? photoLibrary)?.releaseAsset(assetId: assetId)
    }

    // ── Location ─────────────────────────────────────────────────────────────────
    public func locationGetCurrent(requestId: UInt64, accuracy: LocationAccuracy) -> CapStatus {
        guard manifest.isGranted(.location) else { return .notAuthorized }
        guard let location else { return .unavailable }
        location.getCurrent(self, requestId: requestId, accuracy: accuracy)
        return .ok
    }

    public func locationSubscribeUpdates(subscriptionId: UInt64, accuracy: LocationAccuracy) -> CapStatus {
        guard manifest.isGranted(.location) else { return .notAuthorized }
        guard let location else { return .unavailable }
        trackSubscription(subscriptionId, .location)
        location.subscribeUpdates(self, subscriptionId: subscriptionId, accuracy: accuracy)
        return .ok
    }

    // ── Notifications ───────────────────────────────────────────────────────────
    public func notifySchedule(requestId: UInt64, notificationJSON: Data) -> CapStatus {
        guard manifest.isGranted(.notifications) else { return .notAuthorized }
        guard let notifications else { return .unavailable }
        guard let n = CapabilityJSON.decode(LocalNotification.self, from: notificationJSON) else { return .error }
        notifications.schedule(self, requestId: requestId, notification: n)
        return .ok
    }

    public func notifyCancel(id: String) { notifications?.cancel(id: id) }
    public func notifyCancelAll() { notifications?.cancelAll() }

    public func notifySubscribeReceived(subscriptionId: UInt64) -> CapStatus {
        guard manifest.isGranted(.notifications) else { return .notAuthorized }
        guard let notifications else { return .unavailable }
        trackSubscription(subscriptionId, .notifications)
        notifications.subscribeReceived(self, subscriptionId: subscriptionId)
        return .ok
    }

    // ── Biometrics ─────────────────────────────────────────────────────────────
    public func biometricsAvailable() -> Int32 {
        biometrics?.available().rawValue ?? BiometryKind.none.rawValue
    }

    public func biometricsAuthenticate(requestId: UInt64, reason: String, policy: BiometryPolicy) -> CapStatus {
        guard manifest.isGranted(.biometrics) else { return .notAuthorized }
        guard let biometrics else { return .unavailable }
        biometrics.authenticate(self, requestId: requestId, reason: reason, policy: policy)
        return .ok
    }

    // ── Secure storage (síncrono) ────────────────────────────────────────────────
    public func securePut(key: String, value: Data, options: SecurePutOptions) -> CapStatus {
        guard manifest.isGranted(.secureStorage) else { return .notAuthorized }
        guard let secureStorage else { return .unavailable }
        return secureStorage.put(key: key, value: value, options: options)
    }

    public func secureGet(key: String) -> Result<Data, CapStatus> {
        guard manifest.isGranted(.secureStorage) else { return .failure(.notAuthorized) }
        guard let secureStorage else { return .failure(.unavailable) }
        return secureStorage.get(key: key)
    }

    public func secureDelete(key: String) -> CapStatus {
        guard manifest.isGranted(.secureStorage) else { return .notAuthorized }
        guard let secureStorage else { return .unavailable }
        return secureStorage.delete(key: key)
    }

    public func secureKeys() -> [String] {
        guard manifest.isGranted(.secureStorage), let secureStorage else { return [] }
        return secureStorage.keys()
    }

    // ── Share ────────────────────────────────────────────────────────────────────
    public func sharePresent(requestId: UInt64, itemsJSON: Data) -> CapStatus {
        guard manifest.isGranted(.share) else { return .notAuthorized }
        guard let share else { return .unavailable }
        guard let items = CapabilityJSON.decode([ShareItem].self, from: itemsJSON) else { return .error }
        share.present(self, requestId: requestId, items: items)
        return .ok
    }

    // ── Clipboard (síncrono) ──────────────────────────────────────────────────────
    public func clipboardWriteText(_ text: String) -> CapStatus {
        guard manifest.isGranted(.clipboard) else { return .notAuthorized }
        guard let clipboard else { return .unavailable }
        return clipboard.writeText(text)
    }

    public func clipboardReadText() -> String? {
        guard manifest.isGranted(.clipboard) else { return nil }
        return clipboard?.readText()
    }

    public func clipboardHasText() -> Bool {
        guard manifest.isGranted(.clipboard) else { return false }
        return clipboard?.hasText() ?? false
    }

    // ── Haptics (fire-and-forget) ─────────────────────────────────────────────────
    public func hapticsImpact(style: Int32) {
        guard manifest.isGranted(.haptics) else { return }
        haptics?.impact(style: style)
    }
    public func hapticsNotification(kind: Int32) {
        guard manifest.isGranted(.haptics) else { return }
        haptics?.notification(kind: kind)
    }
    public func hapticsSelection() {
        guard manifest.isGranted(.haptics) else { return }
        haptics?.selection()
    }

    // ── Bluetooth ─────────────────────────────────────────────────────────────────
    public func bleState() -> Int32 { bluetooth?.state() ?? 0 }

    public func bleStartScan(subscriptionId: UInt64, filterJSON: Data) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        guard let filter = CapabilityJSON.decode(BleScanFilter.self, from: filterJSON) else { return .error }
        trackSubscription(subscriptionId, .bluetooth)
        return bluetooth.startScan(self, subscriptionId: subscriptionId, filter: filter)
    }

    public func bleConnect(requestId: UInt64, peripheralId: String) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        bluetooth.connect(self, requestId: requestId, peripheralId: peripheralId)
        return .ok
    }

    public func bleDisconnect(peripheralId: String) { bluetooth?.disconnect(peripheralId: peripheralId) }

    public func bleDiscover(requestId: UInt64, peripheralId: String) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        bluetooth.discover(self, requestId: requestId, peripheralId: peripheralId)
        return .ok
    }

    public func bleReadCharacteristic(requestId: UInt64, peripheralId: String, characteristic: String) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        bluetooth.readCharacteristic(self, requestId: requestId, peripheralId: peripheralId, characteristic: characteristic)
        return .ok
    }

    public func bleWriteCharacteristic(requestId: UInt64, peripheralId: String, characteristic: String, value: Data, withResponse: Bool) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        bluetooth.writeCharacteristic(self, requestId: requestId, peripheralId: peripheralId, characteristic: characteristic, value: value, withResponse: withResponse)
        return .ok
    }

    public func bleSubscribeCharacteristic(subscriptionId: UInt64, peripheralId: String, characteristic: String) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        trackSubscription(subscriptionId, .bluetooth)
        return bluetooth.subscribeCharacteristic(self, subscriptionId: subscriptionId, peripheralId: peripheralId, characteristic: characteristic)
    }

    public func bleSubscribeConnection(subscriptionId: UInt64, peripheralId: String) -> CapStatus {
        guard manifest.isGranted(.bluetooth) else { return .notAuthorized }
        guard let bluetooth else { return .unavailable }
        trackSubscription(subscriptionId, .bluetooth)
        return bluetooth.subscribeConnection(self, subscriptionId: subscriptionId, peripheralId: peripheralId)
    }

    // ── Streaming genérico ────────────────────────────────────────────────────────
    public func unsubscribe(_ subscriptionId: UInt64) {
        lock.lock()
        let cap = subscriptions.removeValue(forKey: subscriptionId)
        lock.unlock()
        guard let cap, let p = provider(for: cap) else { return }
        p.cancelSubscription(subscriptionId)
    }
}
