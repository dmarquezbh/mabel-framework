#if canImport(UIKit)
import Foundation
import CoreBluetooth

// =============================================================================
// bluetooth (BLE central) — impl nativa iOS (CoreBluetooth).
// Exercita OS DOIS padrões da ABI:
//   • ONE-SHOT: connect, discover, read-char, write-char.
//   • STREAM:   start-scan (device-found), subscribe-char (char-changed),
//               subscribe-connection (connection-changed).
// Exige NSBluetoothAlwaysUsageDescription. ⚠️ BLE exige DEVICE real (o simulador
// não tem rádio) — impl completa, validação no device.
// =============================================================================

public final class BluetoothCapability: NSObject, BluetoothProviding, CBCentralManagerDelegate, CBPeripheralDelegate {
    public let capabilityId: CapabilityId = .bluetooth
    private var central: CBCentralManager!
    private let queue = DispatchQueue(label: "mabel.ble")
    private let lock = NSLock()

    private var responderRef: CapabilityResponder?

    // Descobertos/conectados (CoreBluetooth não retém peripherals).
    private var peripherals: [String: CBPeripheral] = [:]

    // Streams
    private var scan: (subId: UInt64, filter: BleScanFilter)?
    private var notifyStreams: [UInt64: (peripheralId: String, charUUID: String)] = [:]   // subId → alvo
    private var connectionStreams: [UInt64: String] = [:]                                  // subId → peripheralId

    // One-shots pendentes
    private var connects: [String: (requestId: UInt64)] = [:]                              // peripheralId → reqId
    private var discovers: [String: (requestId: UInt64, remaining: Int, services: [BleService])] = [:]
    private var reads: [String: (requestId: UInt64)] = [:]                                 // "pid|charUUID" → reqId
    private var writes: [String: (requestId: UInt64)] = [:]

    public override init() {
        super.init()
        central = CBCentralManager(delegate: self, queue: queue)
    }

    // MARK: Permission / state

    public func state() -> Int32 {
        switch central.state {
        case .unknown, .resetting: return 0
        case .unsupported: return 1
        case .unauthorized: return 2
        case .poweredOff: return 3
        case .poweredOn: return 4
        @unknown default: return 0
        }
    }

    public func permissionState() -> PermissionState {
        switch CBManager.authorization {
        case .allowedAlways: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    // MARK: Scan (stream)

    public func startScan(_ responder: CapabilityResponder, subscriptionId: UInt64, filter: BleScanFilter) -> CapStatus {
        guard central.state == .poweredOn else { return .unavailable }
        lock.lock(); responderRef = responder; scan = (subscriptionId, filter); lock.unlock()
        let services = filter.serviceUuids.isEmpty ? nil : filter.serviceUuids.map { CBUUID(string: $0) }
        central.scanForPeripherals(withServices: services,
            options: [CBCentralManagerScanOptionAllowDuplicatesKey: filter.allowDuplicates])
        return .ok
    }

    // MARK: Connect / discover / read / write (one-shot)

    public func connect(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String) {
        lock.lock(); responderRef = responder
        guard let p = peripherals[peripheralId] else { lock.unlock(); responder.respond(requestId, .bluetooth, .unavailable, payload: nil); return }
        connects[peripheralId] = (requestId); lock.unlock()
        p.delegate = self
        central.connect(p, options: nil)
    }

    public func disconnect(peripheralId: String) {
        lock.lock(); let p = peripherals[peripheralId]; lock.unlock()
        if let p { central.cancelPeripheralConnection(p) }
    }

    public func discover(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String) {
        lock.lock(); responderRef = responder
        guard let p = peripherals[peripheralId] else { lock.unlock(); responder.respond(requestId, .bluetooth, .unavailable, payload: nil); return }
        discovers[peripheralId] = (requestId, -1, []); lock.unlock()
        p.discoverServices(nil)
    }

    public func readCharacteristic(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String, characteristic: String) {
        lock.lock(); responderRef = responder
        guard let p = peripherals[peripheralId], let ch = findChar(p, characteristic) else {
            lock.unlock(); responder.respond(requestId, .bluetooth, .unavailable, payload: nil); return
        }
        reads["\(peripheralId)|\(characteristic.lowercased())"] = (requestId); lock.unlock()
        p.readValue(for: ch)
    }

    public func writeCharacteristic(_ responder: CapabilityResponder, requestId: UInt64, peripheralId: String, characteristic: String, value: Data, withResponse: Bool) {
        lock.lock(); responderRef = responder
        guard let p = peripherals[peripheralId], let ch = findChar(p, characteristic) else {
            lock.unlock(); responder.respond(requestId, .bluetooth, .unavailable, payload: nil); return
        }
        if withResponse { writes["\(peripheralId)|\(characteristic.lowercased())"] = (requestId) }
        lock.unlock()
        p.writeValue(value, for: ch, type: withResponse ? .withResponse : .withoutResponse)
        if !withResponse { responder.respond(requestId, .bluetooth, .ok, payload: nil) }
    }

    // MARK: Notify / connection (stream)

    public func subscribeCharacteristic(_ responder: CapabilityResponder, subscriptionId: UInt64, peripheralId: String, characteristic: String) -> CapStatus {
        lock.lock(); responderRef = responder
        guard let p = peripherals[peripheralId], let ch = findChar(p, characteristic) else {
            lock.unlock(); return .unavailable
        }
        notifyStreams[subscriptionId] = (peripheralId, characteristic.lowercased()); lock.unlock()
        p.setNotifyValue(true, for: ch)
        return .ok
    }

    public func subscribeConnection(_ responder: CapabilityResponder, subscriptionId: UInt64, peripheralId: String) -> CapStatus {
        lock.lock(); responderRef = responder; connectionStreams[subscriptionId] = peripheralId; lock.unlock()
        return .ok
    }

    public func cancelSubscription(_ subscriptionId: UInt64) {
        lock.lock()
        if scan?.subId == subscriptionId { scan = nil; central.stopScan() }
        if let target = notifyStreams.removeValue(forKey: subscriptionId),
           let p = peripherals[target.peripheralId], let ch = findChar(p, target.charUUID) {
            p.setNotifyValue(false, for: ch)
        }
        connectionStreams.removeValue(forKey: subscriptionId)
        lock.unlock()
    }

    // MARK: Helpers

    private func findChar(_ p: CBPeripheral, _ uuid: String) -> CBCharacteristic? {
        let target = uuid.lowercased()
        for s in p.services ?? [] {
            for c in s.characteristics ?? [] where c.uuid.uuidString.lowercased() == target {
                return c
            }
        }
        return nil
    }

    private func props(_ p: CBCharacteristicProperties) -> UInt32 {
        var v: UInt32 = 0
        if p.contains(.read) { v |= 1 }
        if p.contains(.write) { v |= 2 }
        if p.contains(.writeWithoutResponse) { v |= 4 }
        if p.contains(.notify) { v |= 8 }
        if p.contains(.indicate) { v |= 16 }
        return v
    }

    // MARK: CBCentralManagerDelegate

    public func centralManagerDidUpdateState(_ central: CBCentralManager) { /* estado consultado via state() */ }

    public func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                               advertisementData: [String: Any], rssi RSSI: NSNumber) {
        let pid = peripheral.identifier.uuidString
        lock.lock(); peripherals[pid] = peripheral; let s = scan; let responder = responderRef; lock.unlock()
        guard let s, let responder else { return }
        let serviceUUIDs = (advertisementData[CBAdvertisementDataServiceUUIDsKey] as? [CBUUID])?.map { $0.uuidString } ?? []
        let mfg = advertisementData[CBAdvertisementDataManufacturerDataKey] as? Data
        let adv = BleAdvertisement(
            peripheralId: pid,
            name: peripheral.name ?? (advertisementData[CBAdvertisementDataLocalNameKey] as? String),
            rssi: RSSI.int32Value,
            serviceUuids: serviceUUIDs,
            manufacturerDataBase64: mfg?.base64EncodedString())
        responder.emit(s.subId, .bluetooth, BleEventKind.deviceFound.rawValue, payload: CapabilityJSON.encode(adv))
    }

    public func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        let pid = peripheral.identifier.uuidString
        lock.lock(); let pending = connects.removeValue(forKey: pid); let responder = responderRef
        let connStreams = connectionStreams.filter { $0.value == pid }; lock.unlock()
        pending.map { responder?.respond($0.requestId, .bluetooth, .ok, payload: nil) }
        for (subId, _) in connStreams { responder?.emit(subId, .bluetooth, BleEventKind.connectionChanged.rawValue, payload: Data([1])) }
    }

    public func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        let pid = peripheral.identifier.uuidString
        lock.lock(); let pending = connects.removeValue(forKey: pid); let responder = responderRef; lock.unlock()
        pending.map { responder?.respond($0.requestId, .bluetooth, .error, payload: nil) }
    }

    public func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        let pid = peripheral.identifier.uuidString
        lock.lock(); let responder = responderRef; let connStreams = connectionStreams.filter { $0.value == pid }; lock.unlock()
        for (subId, _) in connStreams { responder?.emit(subId, .bluetooth, BleEventKind.connectionChanged.rawValue, payload: Data([0])) }
    }

    // MARK: CBPeripheralDelegate

    public func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        let pid = peripheral.identifier.uuidString
        let services = peripheral.services ?? []
        lock.lock()
        if var d = discovers[pid] {
            d.remaining = services.count
            d.services = []
            discovers[pid] = d
        }
        lock.unlock()
        if services.isEmpty {
            lock.lock(); let d = discovers.removeValue(forKey: pid); let responder = responderRef; lock.unlock()
            if let d { responder?.respond(d.requestId, .bluetooth, .ok, payload: CapabilityJSON.encode(BleGatt(peripheralId: pid, services: []))) }
            return
        }
        for s in services { peripheral.discoverCharacteristics(nil, for: s) }
    }

    public func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        let pid = peripheral.identifier.uuidString
        let chars = (service.characteristics ?? []).map { BleCharacteristic(uuid: $0.uuid.uuidString, properties: props($0.properties)) }
        var finished: (requestId: UInt64, services: [BleService])?
        lock.lock()
        if var d = discovers[pid] {
            d.services.append(BleService(uuid: service.uuid.uuidString, characteristics: chars))
            d.remaining -= 1
            if d.remaining <= 0 { finished = (d.requestId, d.services); discovers[pid] = nil }
            else { discovers[pid] = d }
        }
        let responder = responderRef
        lock.unlock()
        if let finished {
            responder?.respond(finished.requestId, .bluetooth, .ok,
                               payload: CapabilityJSON.encode(BleGatt(peripheralId: pid, services: finished.services)))
        }
    }

    public func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        let pid = peripheral.identifier.uuidString
        let key = "\(pid)|\(characteristic.uuid.uuidString.lowercased())"
        let value = characteristic.value ?? Data()
        lock.lock()
        let read = reads.removeValue(forKey: key)
        let notifySub = notifyStreams.first { $0.value.peripheralId == pid && $0.value.charUUID == characteristic.uuid.uuidString.lowercased() }?.key
        let responder = responderRef
        lock.unlock()
        if let read {
            responder?.respond(read.requestId, .bluetooth, error == nil ? .ok : .error, payload: error == nil ? value : nil)
        }
        if let notifySub, error == nil {
            responder?.emit(notifySub, .bluetooth, BleEventKind.characteristicChanged.rawValue, payload: value)
        }
    }

    public func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
        let pid = peripheral.identifier.uuidString
        let key = "\(pid)|\(characteristic.uuid.uuidString.lowercased())"
        lock.lock(); let write = writes.removeValue(forKey: key); let responder = responderRef; lock.unlock()
        write.map { responder?.respond($0.requestId, .bluetooth, error == nil ? .ok : .error, payload: nil) }
    }
}
#endif
