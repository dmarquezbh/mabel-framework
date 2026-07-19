#if canImport(UIKit)
import UIKit

// =============================================================================
// Registry — monta um CapabilityHost com todas as impls nativas iOS ligadas.
// Um só ponto: `CapabilityRegistry.makeHost(manifest:bridge:)`. O adapter de
// runtime (WasmKit) e o harness usam a mesma fábrica.
// =============================================================================

/// Fornece o UIViewController de topo para as capabilities que apresentam UI
/// (camera, photo picker, share sheet).
public protocol CapabilityPresenter: AnyObject {
    func topViewController() -> UIViewController?
}

/// Presenter padrão: acha o topo a partir da key window ativa.
public final class WindowScenePresenter: CapabilityPresenter {
    public init() {}
    public func topViewController() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let keyWindow = scenes.flatMap { $0.windows }.first { $0.isKeyWindow }
            ?? scenes.first?.windows.first
        var top = keyWindow?.rootViewController
        while let presented = top?.presentedViewController { top = presented }
        return top
    }
}

public enum CapabilityRegistry {
    /// Monta o host com todas as capabilities. O bundle serve o serviço do
    /// keychain (bundle id) e o app id do manifesto.
    public static func makeHost(manifest: CapabilityManifest,
                                bridge: GuestBridge,
                                presenter: CapabilityPresenter = WindowScenePresenter(),
                                keychainService: String? = nil) -> CapabilityHost {
        let host = CapabilityHost(manifest: manifest, bridge: bridge)
        let service = keychainService ?? manifest.appId

        host.camera = CameraCapability(id: .camera, presenter: presenter)
        host.photoLibrary = CameraCapability(id: .photoLibrary, presenter: presenter)
        host.location = LocationCapability()
        host.notifications = NotificationsCapability()
        host.biometrics = BiometricsCapability()
        host.secureStorage = SecureStorageCapability(service: service)
        host.share = ShareCapability(presenter: presenter)
        host.clipboard = ClipboardCapability()
        host.haptics = HapticsCapability()
        host.bluetooth = BluetoothCapability()
        return host
    }
}
#endif
