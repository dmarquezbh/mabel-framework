#if canImport(UIKit)
import Foundation
import Security

// =============================================================================
// secure-storage — impl nativa iOS (Keychain, Security framework).
// Keychain POR-APP (sem access-groups / iCloud). Síncrono. Sem entitlement.
// service = bundle id; cada item é um kSecClassGenericPassword.
// =============================================================================

public final class SecureStorageCapability: NSObject, SecureStorageProviding {
    public let capabilityId: CapabilityId = .secureStorage
    private let service: String

    public init(service: String) { self.service = service }

    private func accessibility(_ a: SecureAccessibility) -> CFString {
        switch a {
        case .whenUnlocked: return kSecAttrAccessibleWhenUnlocked
        case .afterFirstUnlock: return kSecAttrAccessibleAfterFirstUnlock
        case .whenPasscodeSetThisDeviceOnly: return kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly
        }
    }

    public func put(key: String, value: Data, options: SecurePutOptions) -> CapStatus {
        // Remove antes (idempotente).
        _ = delete(key: key)
        var attrs: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecValueData as String: value,
        ]
        if options.requireUserPresence {
            var acError: Unmanaged<CFError>?
            if let ac = SecAccessControlCreateWithFlags(nil, accessibility(options.accessibility),
                                                        .userPresence, &acError) {
                attrs[kSecAttrAccessControl as String] = ac
            } else {
                return .error
            }
        } else {
            attrs[kSecAttrAccessible as String] = accessibility(options.accessibility)
        }
        let status = SecItemAdd(attrs as CFDictionary, nil)
        return status == errSecSuccess ? .ok : .error
    }

    public func get(key: String) -> Result<Data, CapStatus> {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var out: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &out)
        switch status {
        case errSecSuccess:
            if let data = out as? Data { return .success(data) }
            return .failure(.error)
        case errSecItemNotFound:
            return .failure(.unavailable)
        case errSecUserCanceled:
            return .failure(.cancelled)
        default:
            return .failure(.error)
        }
    }

    public func delete(key: String) -> CapStatus {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
        ]
        let status = SecItemDelete(query as CFDictionary)
        return (status == errSecSuccess || status == errSecItemNotFound) ? .ok : .error
    }

    public func keys() -> [String] {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecReturnAttributes as String: true,
            kSecMatchLimit as String: kSecMatchLimitAll,
        ]
        var out: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &out) == errSecSuccess,
              let items = out as? [[String: Any]] else { return [] }
        return items.compactMap { $0[kSecAttrAccount as String] as? String }
    }
}
#endif
