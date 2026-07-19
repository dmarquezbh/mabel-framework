import Foundation

// =============================================================================
// InProcessGuestBridge — GuestBridge fake, in-process, para o harness/testes.
//
// NÃO há guest .wasm: simula a memória linear com um Data crescente e, em vez de
// invocar exports do guest, CAPTURA os resultados/eventos num log inspecionável.
// Prova a impl nativa das capabilities ponta-a-ponta (host function → API iOS →
// callback) sem um runtime WASM. O adapter real (WasmKit) substitui esta classe.
//
// PLATFORM-AGNOSTIC (Foundation apenas).
// =============================================================================

public final class InProcessGuestBridge: GuestBridge {
    /// Um resultado one-shot capturado.
    public struct ResultRecord {
        public let requestId: UInt64
        public let capability: CapabilityId?
        public let status: CapStatus?
        public let payload: Data?
    }
    /// Um evento de stream capturado.
    public struct EventRecord {
        public let subscriptionId: UInt64
        public let capability: CapabilityId?
        public let eventKind: UInt32
        public let payload: Data?
    }

    public private(set) var results: [ResultRecord] = []
    public private(set) var events: [EventRecord] = []

    /// Hooks opcionais (o harness usa pra atualizar a UI ao vivo).
    public var onResult: ((ResultRecord) -> Void)?
    public var onEvent: ((EventRecord) -> Void)?

    private var memory = Data()
    private let lock = NSLock()

    public init() {}

    // MARK: GuestBridge

    public func allocate(_ length: Int) -> UInt32 {
        lock.lock(); defer { lock.unlock() }
        let ptr = UInt32(memory.count)
        memory.append(Data(count: length))
        return ptr == 0 ? 1 : ptr   // 0 é reservado p/ "sem payload"; começa em 1
    }

    public func write(_ bytes: [UInt8], to pointer: UInt32) {
        lock.lock(); defer { lock.unlock() }
        let start = Int(pointer)
        guard start + bytes.count <= memory.count else { return }
        memory.replaceSubrange(start..<start + bytes.count, with: bytes)
    }

    public func read(pointer: UInt32, length: UInt32) -> [UInt8] {
        lock.lock(); defer { lock.unlock() }
        let start = Int(pointer), end = min(Int(pointer) + Int(length), memory.count)
        guard start < end else { return [] }
        return [UInt8](memory[start..<end])
    }

    public func invokeResult(requestId: UInt64, capability: Int32, status: Int32,
                             payloadPtr: UInt32, payloadLen: UInt32) {
        let payload = payloadLen > 0 ? Data(read(pointer: payloadPtr, length: payloadLen)) : nil
        let rec = ResultRecord(requestId: requestId, capability: CapabilityId(rawValue: capability),
                               status: CapStatus(rawValue: status), payload: payload)
        lock.lock(); results.append(rec); lock.unlock()
        onResult?(rec)
    }

    public func invokeEvent(subscriptionId: UInt64, capability: Int32, eventKind: UInt32,
                            payloadPtr: UInt32, payloadLen: UInt32) {
        let payload = payloadLen > 0 ? Data(read(pointer: payloadPtr, length: payloadLen)) : nil
        let rec = EventRecord(subscriptionId: subscriptionId, capability: CapabilityId(rawValue: capability),
                              eventKind: eventKind, payload: payload)
        lock.lock(); events.append(rec); lock.unlock()
        onEvent?(rec)
    }

    // MARK: Test helpers

    public func reset() {
        lock.lock(); results.removeAll(); events.removeAll(); memory.removeAll(); lock.unlock()
    }
}
