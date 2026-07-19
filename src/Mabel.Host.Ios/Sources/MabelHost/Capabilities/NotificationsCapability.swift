#if canImport(UIKit)
import Foundation
import UserNotifications

// =============================================================================
// notifications (LOCAL) — impl nativa iOS (UserNotifications).
//   • schedule — one-shot (agenda; resolve com o id agendado).
//   • subscribeReceived — stream (tap/entrega em foreground via delegate).
// Só local (push/APNs fora do v2). Nenhum entitlement; permissão via
// requestAuthorization.
// =============================================================================

public final class NotificationsCapability: NSObject, NotificationsProviding, UNUserNotificationCenterDelegate {
    public let capabilityId: CapabilityId = .notifications
    private let center = UNUserNotificationCenter.current()
    private var receivedStream: (responder: CapabilityResponder, subscriptionId: UInt64)?
    private let lock = NSLock()

    public override init() {
        super.init()
        center.delegate = self
    }

    // MARK: Permission

    public func permissionState() -> PermissionState {
        var result: PermissionState = .notDetermined
        let sem = DispatchSemaphore(value: 0)
        center.getNotificationSettings { settings in
            switch settings.authorizationStatus {
            case .authorized, .provisional, .ephemeral: result = .granted
            case .denied: result = .denied
            case .notDetermined: result = .notDetermined
            @unknown default: result = .denied
            }
            sem.signal()
        }
        _ = sem.wait(timeout: .now() + 2)
        return result
    }

    public func requestPermission(_ responder: CapabilityResponder, requestId: UInt64) {
        center.requestAuthorization(options: [.alert, .sound, .badge]) { granted, _ in
            let state: PermissionState = granted ? .granted : .denied
            responder.respond(requestId, .notifications, .ok, payload: Data([UInt8(state.rawValue)]))
        }
    }

    // MARK: Schedule

    public func schedule(_ responder: CapabilityResponder, requestId: UInt64, notification n: LocalNotification) {
        let content = UNMutableNotificationContent()
        content.title = n.title
        content.body = n.body
        content.sound = n.sound.map { UNNotificationSound(named: UNNotificationSoundName($0)) } ?? .default
        if let badge = n.badge { content.badge = NSNumber(value: badge) }

        let trigger: UNNotificationTrigger
        if let secs = n.trigger.afterSeconds {
            trigger = UNTimeIntervalNotificationTrigger(timeInterval: TimeInterval(max(1, secs)), repeats: false)
        } else if let atMs = n.trigger.atTimeMs {
            let date = Date(timeIntervalSince1970: TimeInterval(atMs) / 1000)
            let comps = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
            trigger = UNCalendarNotificationTrigger(dateMatching: comps, repeats: false)
        } else {
            trigger = UNTimeIntervalNotificationTrigger(timeInterval: 1, repeats: false)
        }

        let request = UNNotificationRequest(identifier: n.id, content: content, trigger: trigger)
        center.add(request) { error in
            responder.respond(requestId, .notifications, error == nil ? .ok : .error, payload: nil)
        }
    }

    public func cancel(id: String) {
        center.removePendingNotificationRequests(withIdentifiers: [id])
    }

    public func cancelAll() {
        center.removeAllPendingNotificationRequests()
    }

    public func subscribeReceived(_ responder: CapabilityResponder, subscriptionId: UInt64) {
        lock.lock(); receivedStream = (responder, subscriptionId); lock.unlock()
    }

    public func cancelSubscription(_ subscriptionId: UInt64) {
        lock.lock()
        if receivedStream?.subscriptionId == subscriptionId { receivedStream = nil }
        lock.unlock()
    }

    // MARK: UNUserNotificationCenterDelegate

    /// App em foreground: uma notificação chegou (event-kind = 1).
    public func userNotificationCenter(_ center: UNUserNotificationCenter,
                                       willPresent notification: UNNotification,
                                       withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        emitReceived(id: notification.request.identifier, kind: 1)
        completionHandler([.banner, .sound])
    }

    /// Usuário tocou numa notificação (event-kind = 0).
    public func userNotificationCenter(_ center: UNUserNotificationCenter,
                                       didReceive response: UNNotificationResponse,
                                       withCompletionHandler completionHandler: @escaping () -> Void) {
        emitReceived(id: response.notification.request.identifier, kind: 0)
        completionHandler()
    }

    private func emitReceived(id: String, kind: UInt32) {
        lock.lock(); let s = receivedStream; lock.unlock()
        guard let s else { return }
        s.responder.emit(s.subscriptionId, .notifications, kind, payload: Data(id.utf8))
    }
}
#endif
