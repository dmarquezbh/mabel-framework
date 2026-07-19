import Foundation

// =============================================================================
// Manifesto de capability (lado host) — a declaração capability-based do app.
//
// Espelha src/Mabel.Wasi.Protocol/Capabilities/CapabilityManifest.cs. O host lê
// este JSON (ex.: "mabel.caps.json" no bundle) no load e SÓ liga as capabilities
// declaradas; o resto responde `.notAuthorized` sem tocar o SO.
//
// PLATFORM-AGNOSTIC (Foundation apenas).
// =============================================================================

/// Uma capability concedida, com a justificativa mostrada no prompt do SO e
/// flags opcionais.
public struct CapabilityGrant: Codable, Equatable {
    public let capability: CapabilityId
    public let usageDescription: String?
    public let options: [String: String]?

    public init(capability: CapabilityId, usageDescription: String? = nil,
                options: [String: String]? = nil) {
        self.capability = capability
        self.usageDescription = usageDescription
        self.options = options
    }

    private enum CodingKeys: String, CodingKey {
        case capability, usageDescription, options
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let key = try c.decode(String.self, forKey: .capability)
        guard let cap = CapabilityId(manifestKey: key) else {
            throw DecodingError.dataCorruptedError(
                forKey: .capability, in: c,
                debugDescription: "Capability desconhecida: \(key)")
        }
        self.capability = cap
        self.usageDescription = try c.decodeIfPresent(String.self, forKey: .usageDescription)
        self.options = try c.decodeIfPresent([String: String].self, forKey: .options)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(capability.manifestKey, forKey: .capability)
        try c.encodeIfPresent(usageDescription, forKey: .usageDescription)
        try c.encodeIfPresent(options, forKey: .options)
    }
}

/// Manifesto do app. Transporte = JSON (mesmo shape do C# / SduiDocument).
public struct CapabilityManifest: Codable, Equatable {
    public let schemaVersion: Int
    public let appId: String
    public let grants: [CapabilityGrant]

    public init(schemaVersion: Int = 1, appId: String, grants: [CapabilityGrant] = []) {
        self.schemaVersion = schemaVersion
        self.appId = appId
        self.grants = grants
    }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, appId, grants
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        self.appId = try c.decode(String.self, forKey: .appId)
        self.grants = try c.decodeIfPresent([CapabilityGrant].self, forKey: .grants) ?? []
    }

    /// True se a capability foi declarada (helper de atenuação do host).
    public func isGranted(_ capability: CapabilityId) -> Bool {
        grants.contains { $0.capability == capability }
    }

    public func grant(for capability: CapabilityId) -> CapabilityGrant? {
        grants.first { $0.capability == capability }
    }

    // MARK: - Loading

    /// Decodifica de bytes JSON. Erros propagam (host trata como manifesto inválido).
    public static func decode(from data: Data) throws -> CapabilityManifest {
        try JSONDecoder().decode(CapabilityManifest.self, from: data)
    }

    /// Carrega "mabel.caps.json" do bundle informado. Ausência = manifesto vazio
    /// (app puramente SDUI, zero device access) — decisão fail-closed.
    public static func loadFromBundle(_ bundle: Bundle, name: String = "mabel.caps",
                                      appId: String) -> CapabilityManifest {
        guard let url = bundle.url(forResource: name, withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let manifest = try? decode(from: data)
        else {
            return CapabilityManifest(appId: appId, grants: [])
        }
        return manifest
    }
}
