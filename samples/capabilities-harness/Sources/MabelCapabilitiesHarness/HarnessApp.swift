import SwiftUI
import MabelHost

// =============================================================================
// Mabel Capabilities Harness — app iOS que exercita CADA capability chamando o
// CapabilityHost DIRETO (sem guest .wasm). Prova as impls nativas no device.
//
// Build/deploy do Linux/WSL (precisa do Darwin SDK instalado):
//   cd samples/capabilities-harness && xtool dev build   # ou `xtool dev run`
//
// O manifesto aqui concede TODAS as capabilities (sem free-gating — o que a conta
// FREE bloqueia se descobre rodando). O InProcessGuestBridge captura os
// resultados/eventos e a UI os mostra ao vivo.
// =============================================================================

@main
struct MabelCapabilitiesHarnessApp: App {
    var body: some Scene {
        WindowGroup { HarnessView() }
    }
}

/// Modelo: monta o host com bridge in-process e roda os testes por capability.
@MainActor
final class HarnessModel: ObservableObject {
    @Published var log: [String] = ["Pronto. Toque numa capability."]

    private let bridge = InProcessGuestBridge()
    private let host: CapabilityHost
    private var nextId: UInt64 = 1

    init() {
        // Manifesto = todas as capabilities concedidas.
        let grants = CapabilityId.allCases.map {
            CapabilityGrant(capability: $0, usageDescription: "harness")
        }
        let manifest = CapabilityManifest(appId: "com.mabel.capabilities.harness", grants: grants)
        host = CapabilityRegistry.makeHost(manifest: manifest, bridge: bridge)

        bridge.onResult = { [weak self] r in
            let cap = r.capability.map { "\($0)" } ?? "?"
            let st = r.status.map { "\($0)" } ?? "?"
            let body = r.payload.flatMap { String(data: $0, encoding: .utf8) } ?? "\(r.payload?.count ?? 0)B"
            DispatchQueue.main.async { self?.append("← result[\(r.requestId)] \(cap) \(st) \(body)") }
        }
        bridge.onEvent = { [weak self] e in
            let cap = e.capability.map { "\($0)" } ?? "?"
            let body = e.payload.flatMap { String(data: $0, encoding: .utf8) } ?? "\(e.payload?.count ?? 0)B"
            DispatchQueue.main.async { self?.append("↞ event[\(e.subscriptionId)] \(cap) k=\(e.eventKind) \(body)") }
        }
    }

    private func append(_ s: String) { log.insert(s, at: 0); if log.count > 200 { log.removeLast() } }
    private func id() -> UInt64 { defer { nextId += 1 }; return nextId }
    private func json<T: Encodable>(_ v: T) -> Data { CapabilityJSON.encode(v) ?? Data() }

    // MARK: - Testes por capability (chamam host functions direto)

    func run(_ test: HarnessTest) {
        append("→ \(test.label)")
        let rid = id()
        switch test {
        case .cameraCapture:
            let opts = CaptureOptions(kind: .photo, facing: .back, quality: 0.8, allowEdit: false)
            append("status=\(host.cameraCapture(requestId: rid, optionsJSON: json(opts)))")
        case .photoPick:
            let opts = PickerOptions(kind: .photo, maxItems: 1)
            append("status=\(host.cameraPick(requestId: rid, optionsJSON: json(opts)))")
        case .locationCurrent:
            append("status=\(host.locationGetCurrent(requestId: rid, accuracy: .balanced))")
        case .locationStream:
            append("sub=\(rid) status=\(host.locationSubscribeUpdates(subscriptionId: rid, accuracy: .balanced))")
        case .notify:
            let n = LocalNotification(id: "harness-\(rid)", title: "Mabel", body: "Oi do harness",
                                      sound: nil, badge: nil,
                                      trigger: NotificationTrigger(afterSeconds: 3, atTimeMs: nil))
            append("status=\(host.notifySchedule(requestId: rid, notificationJSON: json(n)))")
        case .notifyStream:
            append("sub=\(rid) status=\(host.notifySubscribeReceived(subscriptionId: rid))")
        case .biometricsAvailable:
            append("biometry=\(host.biometricsAvailable())")
        case .biometricsAuth:
            append("status=\(host.biometricsAuthenticate(requestId: rid, reason: "Provar biometria", policy: .biometricsOrPasscode))")
        case .securePutGet:
            let put = host.securePut(key: "harness-key", value: Data("segredo".utf8),
                                     options: SecurePutOptions(accessibility: .whenUnlocked, requireUserPresence: false))
            let get = host.secureGet(key: "harness-key")
            let got: String
            switch get { case .success(let d): got = String(data: d, encoding: .utf8) ?? "\(d.count)B"
                         case .failure(let s): got = "erro \(s)" }
            append("put=\(put) get=\(got) keys=\(host.secureKeys())")
        case .share:
            let items = [ShareItem(text: "Compartilhado pelo Mabel harness", url: nil, assetId: nil, blob: nil)]
            append("status=\(host.sharePresent(requestId: rid, itemsJSON: json(items)))")
        case .clipboard:
            _ = host.clipboardWriteText("mabel-clip-\(rid)")
            append("hasText=\(host.clipboardHasText()) read=\(host.clipboardReadText() ?? "nil")")
        case .haptics:
            host.hapticsImpact(style: 1); host.hapticsNotification(kind: 0); host.hapticsSelection()
            append("haptics disparados")
        case .bleState:
            append("adapter-state=\(host.bleState())")
        case .bleScan:
            let f = BleScanFilter(serviceUuids: [], allowDuplicates: false)
            append("sub=\(rid) status=\(host.bleStartScan(subscriptionId: rid, filterJSON: json(f)))")
        }
    }
}

/// Os testes disponíveis no harness.
enum HarnessTest: String, CaseIterable, Identifiable {
    case cameraCapture, photoPick, locationCurrent, locationStream, notify, notifyStream
    case biometricsAvailable, biometricsAuth, securePutGet, share, clipboard, haptics
    case bleState, bleScan
    var id: String { rawValue }

    var label: String {
        switch self {
        case .cameraCapture: return "Camera — capturar foto ⚠️device"
        case .photoPick: return "Photo — picker da galeria"
        case .locationCurrent: return "Location — posição atual ⚠️device"
        case .locationStream: return "Location — stream de updates ⚠️device"
        case .notify: return "Notifications — agendar local (+3s)"
        case .notifyStream: return "Notifications — stream de recebidas"
        case .biometricsAvailable: return "Biometrics — tipo disponível"
        case .biometricsAuth: return "Biometrics — autenticar ⚠️device"
        case .securePutGet: return "Secure storage — put + get (Keychain)"
        case .share: return "Share — sheet nativo"
        case .clipboard: return "Clipboard — write + read"
        case .haptics: return "Haptics — impact/notify/selection ⚠️device"
        case .bleState: return "Bluetooth — estado do rádio"
        case .bleScan: return "Bluetooth — scan (stream) ⚠️device"
        }
    }
}
