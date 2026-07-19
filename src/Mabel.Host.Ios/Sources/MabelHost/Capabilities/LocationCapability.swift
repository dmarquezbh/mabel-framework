#if canImport(UIKit)
import Foundation
import CoreLocation

// =============================================================================
// location — impl nativa iOS (CoreLocation / CLLocationManager).
//   • getCurrent — one-shot (requestLocation → um fix → on-capability-result).
//   • subscribeUpdates — stream (startUpdatingLocation → N on-capability-event).
// when-in-use apenas (v2). Exige NSLocationWhenInUseUsageDescription.
// =============================================================================

public final class LocationCapability: NSObject, LocationProviding, CLLocationManagerDelegate {
    public let capabilityId: CapabilityId = .location
    private let manager = CLLocationManager()

    private var oneShots: [(responder: CapabilityResponder, requestId: UInt64)] = []
    private var stream: (responder: CapabilityResponder, subscriptionId: UInt64)?
    private var permissionRequests: [(responder: CapabilityResponder, requestId: UInt64)] = []
    private let lock = NSLock()

    public override init() {
        super.init()
        manager.delegate = self
    }

    // MARK: Permission

    public func permissionState() -> PermissionState {
        switch manager.authorizationStatus {
        case .authorizedWhenInUse, .authorizedAlways: return .granted
        case .denied: return .denied
        case .restricted: return .restricted
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    public func requestPermission(_ responder: CapabilityResponder, requestId: UInt64) {
        let state = permissionState()
        if state != .notDetermined {
            responder.respond(requestId, .location, .ok, payload: Data([UInt8(state.rawValue)]))
            return
        }
        lock.lock(); permissionRequests.append((responder, requestId)); lock.unlock()
        manager.requestWhenInUseAuthorization()
    }

    // MARK: One-shot + stream

    private func applyAccuracy(_ accuracy: LocationAccuracy) {
        switch accuracy {
        case .coarse: manager.desiredAccuracy = kCLLocationAccuracyThreeKilometers
        case .balanced: manager.desiredAccuracy = kCLLocationAccuracyHundredMeters
        case .precise: manager.desiredAccuracy = kCLLocationAccuracyBest
        }
    }

    public func getCurrent(_ responder: CapabilityResponder, requestId: UInt64, accuracy: LocationAccuracy) {
        applyAccuracy(accuracy)
        lock.lock(); oneShots.append((responder, requestId)); lock.unlock()
        manager.requestLocation()
    }

    public func subscribeUpdates(_ responder: CapabilityResponder, subscriptionId: UInt64, accuracy: LocationAccuracy) {
        applyAccuracy(accuracy)
        lock.lock(); stream = (responder, subscriptionId); lock.unlock()
        manager.startUpdatingLocation()
    }

    public func cancelSubscription(_ subscriptionId: UInt64) {
        lock.lock()
        if stream?.subscriptionId == subscriptionId { stream = nil; manager.stopUpdatingLocation() }
        lock.unlock()
    }

    // MARK: CLLocationManagerDelegate

    private func position(from loc: CLLocation) -> Position {
        Position(
            latitude: loc.coordinate.latitude,
            longitude: loc.coordinate.longitude,
            accuracyM: loc.horizontalAccuracy,
            altitudeM: loc.verticalAccuracy >= 0 ? loc.altitude : nil,
            headingDeg: loc.course >= 0 ? loc.course : nil,
            speedMps: loc.speed >= 0 ? loc.speed : nil,
            timestampMs: UInt64(loc.timestamp.timeIntervalSince1970 * 1000))
    }

    public func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        let payload = CapabilityJSON.encode(position(from: loc))
        lock.lock()
        let shots = oneShots; oneShots.removeAll()
        let s = stream
        lock.unlock()
        for shot in shots { shot.responder.respond(shot.requestId, .location, .ok, payload: payload) }
        if let s { s.responder.emit(s.subscriptionId, .location, 0, payload: payload) }
    }

    public func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        lock.lock(); let shots = oneShots; oneShots.removeAll(); lock.unlock()
        for shot in shots { shot.responder.respond(shot.requestId, .location, .error, payload: nil) }
    }

    public func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let state = permissionState()
        guard state != .notDetermined else { return }
        lock.lock(); let reqs = permissionRequests; permissionRequests.removeAll(); lock.unlock()
        for r in reqs { r.responder.respond(r.requestId, .location, .ok, payload: Data([UInt8(state.rawValue)])) }
    }
}
#endif
