#if canImport(UIKit)
import UIKit

// =============================================================================
// share + clipboard + haptics — impls nativas iOS. Sem entitlement.
//   • share:    UIActivityViewController (precisa de presenter).
//   • clipboard: UIPasteboard.general (síncrono).
//   • haptics:   UIFeedbackGenerator (fire-and-forget; device físico).
// =============================================================================

public final class ShareCapability: NSObject, ShareProviding {
    public let capabilityId: CapabilityId = .share
    private let presenter: CapabilityPresenter
    public init(presenter: CapabilityPresenter) { self.presenter = presenter }

    public func present(_ responder: CapabilityResponder, requestId: UInt64, items: [ShareItem]) {
        DispatchQueue.main.async {
            var activityItems: [Any] = []
            for item in items {
                if let text = item.text { activityItems.append(text) }
                else if let urlStr = item.url, let url = URL(string: urlStr) { activityItems.append(url) }
                else if let assetId = item.assetId, let data = AssetStore.shared.data(assetId) {
                    activityItems.append(self.tempFile(data, ext: "jpg") ?? data)
                }
                else if let blob = item.blob, let bytes = Data(base64Encoded: blob.bytesBase64) {
                    let name = blob.filename ?? "mabel-share.bin"
                    activityItems.append(self.tempFile(bytes, filename: name) ?? bytes)
                }
            }
            guard !activityItems.isEmpty, let host = self.presenter.topViewController() else {
                responder.respond(requestId, .share, activityItems.isEmpty ? .error : .error, payload: nil); return
            }
            let vc = UIActivityViewController(activityItems: activityItems, applicationActivities: nil)
            vc.completionWithItemsHandler = { _, completed, _, _ in
                responder.respond(requestId, .share, .ok, payload: Data([completed ? 1 : 0]))
            }
            // iPad: popover precisa de sourceView.
            if let pop = vc.popoverPresentationController {
                pop.sourceView = host.view
                pop.sourceRect = CGRect(x: host.view.bounds.midX, y: host.view.bounds.midY, width: 0, height: 0)
                pop.permittedArrowDirections = []
            }
            host.present(vc, animated: true)
        }
    }

    private func tempFile(_ data: Data, ext: String? = nil, filename: String? = nil) -> URL? {
        let name = filename ?? "mabel-\(UUID().uuidString).\(ext ?? "bin")"
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(name)
        do { try data.write(to: url); return url } catch { return nil }
    }
}

public final class ClipboardCapability: NSObject, ClipboardProviding {
    public let capabilityId: CapabilityId = .clipboard

    public func writeText(_ text: String) -> CapStatus {
        UIPasteboard.general.string = text
        return .ok
    }
    public func readText() -> String? { UIPasteboard.general.string }
    public func hasText() -> Bool { UIPasteboard.general.hasStrings }
}

public final class HapticsCapability: NSObject, HapticsProviding {
    public let capabilityId: CapabilityId = .haptics

    public func impact(style: Int32) {
        DispatchQueue.main.async {
            let mapped: UIImpactFeedbackGenerator.FeedbackStyle
            switch style {
            case 0: mapped = .light
            case 1: mapped = .medium
            case 2: mapped = .heavy
            case 3: mapped = .soft
            case 4: mapped = .rigid
            default: mapped = .medium
            }
            let gen = UIImpactFeedbackGenerator(style: mapped)
            gen.prepare(); gen.impactOccurred()
        }
    }

    public func notification(kind: Int32) {
        DispatchQueue.main.async {
            let mapped: UINotificationFeedbackGenerator.FeedbackType
            switch kind {
            case 0: mapped = .success
            case 1: mapped = .warning
            case 2: mapped = .error
            default: mapped = .success
            }
            let gen = UINotificationFeedbackGenerator()
            gen.prepare(); gen.notificationOccurred(mapped)
        }
    }

    public func selection() {
        DispatchQueue.main.async {
            let gen = UISelectionFeedbackGenerator()
            gen.prepare(); gen.selectionChanged()
        }
    }
}
#endif
