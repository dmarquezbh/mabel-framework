#if canImport(UIKit)
import Foundation
import LocalAuthentication

// =============================================================================
// biometrics — impl nativa iOS (LocalAuthentication / LAContext).
// Só devolve veredito booleano; nenhum dado biométrico cruza o sandbox.
// Exige NSFaceIDUsageDescription (Face ID). ⚠️ Face ID exige DEVICE real.
// =============================================================================

public final class BiometricsCapability: NSObject, BiometricsProviding {
    public let capabilityId: CapabilityId = .biometrics

    public func available() -> BiometryKind {
        let ctx = LAContext()
        var error: NSError?
        guard ctx.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else {
            return .none
        }
        switch ctx.biometryType {
        case .faceID: return .faceID
        case .touchID: return .touchID
        case .opticID: return .opticID
        case .none: return .none
        @unknown default: return .none
        }
    }

    public func permissionState() -> PermissionState {
        // Biometria não tem prompt de permissão prévio; disponibilidade == granted.
        available() == .none ? .denied : .granted
    }

    public func authenticate(_ responder: CapabilityResponder, requestId: UInt64,
                             reason: String, policy: BiometryPolicy) {
        let ctx = LAContext()
        let laPolicy: LAPolicy = policy == .biometricsOrPasscode
            ? .deviceOwnerAuthentication
            : .deviceOwnerAuthenticationWithBiometrics
        var error: NSError?
        guard ctx.canEvaluatePolicy(laPolicy, error: &error) else {
            responder.respond(requestId, .biometrics, .unavailable, payload: nil); return
        }
        ctx.evaluatePolicy(laPolicy, localizedReason: reason) { success, evalError in
            if success {
                responder.respond(requestId, .biometrics, .ok, payload: Data([1]))
            } else if let laError = evalError as? LAError, laError.code == .userCancel || laError.code == .systemCancel {
                responder.respond(requestId, .biometrics, .cancelled, payload: nil)
            } else {
                responder.respond(requestId, .biometrics, .ok, payload: Data([0]))
            }
        }
    }
}
#endif
