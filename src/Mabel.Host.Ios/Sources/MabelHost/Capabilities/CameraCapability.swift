#if canImport(UIKit)
import UIKit
import AVFoundation
import PhotosUI

// =============================================================================
// camera + photo-library — impl nativa iOS.
//   • camera: UIImagePickerController(.camera)  → captura foto.
//   • photo:  PHPickerViewController            → seleção da galeria.
// Resultado = CapturedAsset (metadados + assetId). Os bytes ficam no AssetStore
// e são lidos sob demanda por read-asset (chunked) — não despejados no guest.
//
// ⚠️ Câmera exige DEVICE físico (simulador não tem câmera) + NSCameraUsageDescription.
// =============================================================================

/// Guarda os bytes dos assets capturados/selecionados, indexados por assetId.
final class AssetStore {
    static let shared = AssetStore()
    private var assets: [String: (data: Data, mime: String)] = [:]
    private let lock = NSLock()

    func put(_ data: Data, mime: String) -> String {
        let id = "mabel-asset:" + UUID().uuidString
        lock.lock(); assets[id] = (data, mime); lock.unlock()
        return id
    }
    func data(_ id: String) -> Data? {
        lock.lock(); defer { lock.unlock() }; return assets[id]?.data
    }
    func mime(_ id: String) -> String? {
        lock.lock(); defer { lock.unlock() }; return assets[id]?.mime
    }
    func remove(_ id: String) {
        lock.lock(); assets[id] = nil; lock.unlock()
    }
}

public final class CameraCapability: NSObject, CameraProviding {
    public let capabilityId: CapabilityId
    private let presenter: CapabilityPresenter
    /// Coordinators vivos durante a apresentação (self-retidos aqui).
    private var liveCoordinators: [NSObject] = []
    private let lock = NSLock()

    /// `id` = .camera (captura) ou .photoLibrary (picker). Uma instância por papel.
    public init(id: CapabilityId, presenter: CapabilityPresenter) {
        self.capabilityId = id
        self.presenter = presenter
    }

    private func retain(_ c: NSObject) { lock.lock(); liveCoordinators.append(c); lock.unlock() }
    private func release(_ c: NSObject) { lock.lock(); liveCoordinators.removeAll { $0 === c }; lock.unlock() }

    // MARK: Permission

    public func permissionState() -> PermissionState {
        switch capabilityId {
        case .camera:
            switch AVCaptureDevice.authorizationStatus(for: .video) {
            case .authorized: return .granted
            case .denied: return .denied
            case .restricted: return .restricted
            case .notDetermined: return .notDetermined
            @unknown default: return .denied
            }
        default:
            // PHPicker não exige permissão de leitura; tratamos como granted.
            return .granted
        }
    }

    public func requestPermission(_ responder: CapabilityResponder, requestId: UInt64) {
        guard capabilityId == .camera else {
            responder.respond(requestId, capabilityId, .ok,
                              payload: Data([UInt8(PermissionState.granted.rawValue)]))
            return
        }
        AVCaptureDevice.requestAccess(for: .video) { granted in
            let state: PermissionState = granted ? .granted : .denied
            responder.respond(requestId, .camera, .ok, payload: Data([UInt8(state.rawValue)]))
        }
    }

    // MARK: Capture (camera)

    public func capture(_ responder: CapabilityResponder, requestId: UInt64, options: CaptureOptions) {
        DispatchQueue.main.async {
            guard UIImagePickerController.isSourceTypeAvailable(.camera) else {
                responder.respond(requestId, .camera, .unavailable, payload: nil); return
            }
            guard let host = self.presenter.topViewController() else {
                responder.respond(requestId, .camera, .error, payload: nil); return
            }
            let picker = UIImagePickerController()
            picker.sourceType = .camera
            picker.cameraDevice = options.facing == .front ? .front : .rear
            picker.allowsEditing = options.allowEdit
            picker.mediaTypes = options.kind == .video ? ["public.movie"] : ["public.image"]
            let coord = ImagePickerCoordinator(quality: options.quality) { [weak self] result in
                switch result {
                case .success(let asset):
                    responder.respond(requestId, .camera, .ok, payload: CapabilityJSON.encode(asset))
                case .cancelled:
                    responder.respond(requestId, .camera, .cancelled, payload: nil)
                case .failure:
                    responder.respond(requestId, .camera, .error, payload: nil)
                }
                if let self { self.release(coord) }
            }
            picker.delegate = coord
            self.retain(coord)
            host.present(picker, animated: true)
        }
    }

    // MARK: Pick (photo library)

    public func pick(_ responder: CapabilityResponder, requestId: UInt64, options: PickerOptions) {
        DispatchQueue.main.async {
            guard let host = self.presenter.topViewController() else {
                responder.respond(requestId, .photoLibrary, .error, payload: nil); return
            }
            var config = PHPickerConfiguration()
            config.selectionLimit = Int(options.maxItems)
            config.filter = options.kind == .video ? .videos : .images
            let picker = PHPickerViewController(configuration: config)
            let coord = PhotoPickerCoordinator { [weak self] result in
                switch result {
                case .success(let asset):
                    responder.respond(requestId, .photoLibrary, .ok, payload: CapabilityJSON.encode(asset))
                case .cancelled:
                    responder.respond(requestId, .photoLibrary, .cancelled, payload: nil)
                case .failure:
                    responder.respond(requestId, .photoLibrary, .error, payload: nil)
                }
                if let self, let c = self.pickerCoord { self.release(c) }
                self?.pickerCoord = nil
            }
            picker.delegate = coord
            self.pickerCoord = coord
            self.retain(coord)
            host.present(picker, animated: true)
        }
    }
    private var pickerCoord: PhotoPickerCoordinator?

    // MARK: Read / release asset

    public func readAsset(assetId: String, offset: UInt64, length: UInt32) -> Data? {
        guard let data = AssetStore.shared.data(assetId) else { return nil }
        let start = Int(min(offset, UInt64(data.count)))
        let end = min(start + Int(length), data.count)
        guard start < end else { return Data() }
        return data.subdata(in: start..<end)
    }

    public func releaseAsset(assetId: String) { AssetStore.shared.remove(assetId) }
}

// MARK: - Coordinators

enum CaptureResult { case success(CapturedAsset); case cancelled; case failure }

final class ImagePickerCoordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
    private let quality: Float
    private let done: (CaptureResult) -> Void
    init(quality: Float, done: @escaping (CaptureResult) -> Void) {
        self.quality = quality; self.done = done
    }

    func imagePickerController(_ picker: UIImagePickerController,
                              didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
        picker.dismiss(animated: true)
        let image = (info[.editedImage] as? UIImage) ?? (info[.originalImage] as? UIImage)
        guard let image, let data = image.jpegData(compressionQuality: CGFloat(max(0.1, min(1, quality)))) else {
            done(.failure); return
        }
        let id = AssetStore.shared.put(data, mime: "image/jpeg")
        let asset = CapturedAsset(assetId: id, kind: .photo,
                                  width: UInt32(image.size.width * image.scale),
                                  height: UInt32(image.size.height * image.scale),
                                  byteSize: UInt64(data.count), mime: "image/jpeg")
        done(.success(asset))
    }

    func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
        picker.dismiss(animated: true); done(.cancelled)
    }
}

final class PhotoPickerCoordinator: NSObject, PHPickerViewControllerDelegate {
    private let done: (CaptureResult) -> Void
    init(done: @escaping (CaptureResult) -> Void) { self.done = done }

    func picker(_ picker: PHPickerViewController, didFinishPicking results: [PHPickerResult]) {
        picker.dismiss(animated: true)
        guard let first = results.first else { done(.cancelled); return }
        let provider = first.itemProvider
        guard provider.canLoadObject(ofClass: UIImage.self) else { done(.failure); return }
        provider.loadObject(ofClass: UIImage.self) { object, _ in
            guard let image = object as? UIImage, let data = image.jpegData(compressionQuality: 0.9) else {
                self.done(.failure); return
            }
            let id = AssetStore.shared.put(data, mime: "image/jpeg")
            let asset = CapturedAsset(assetId: id, kind: .photo,
                                      width: UInt32(image.size.width * image.scale),
                                      height: UInt32(image.size.height * image.scale),
                                      byteSize: UInt64(data.count), mime: "image/jpeg")
            self.done(.success(asset))
        }
    }
}
#endif
