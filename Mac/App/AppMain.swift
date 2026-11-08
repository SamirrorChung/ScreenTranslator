import AppKit

@main
enum AppMain {
    static func main() {
        MainActor.assumeIsolated {
            let app = NSApplication.shared
            let delegate = AppDelegate()
            app.delegate = delegate
            withExtendedLifetime(delegate) {
                app.run()
            }
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var model: AppModel?
    private var statusBar: StatusBarController?
    private let showNotification = Notification.Name("com.screentranslator.show")

    func applicationDidFinishLaunching(_ notification: Notification) {
        let bundleID = Bundle.main.bundleIdentifier ?? "com.screentranslator.app"
        let alreadyRunning = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
            .contains { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }
        if alreadyRunning {
            DistributedNotificationCenter.default().post(name: showNotification, object: nil)
            NSApp.terminate(nil)
            return
        }

        DistributedNotificationCenter.default().addObserver(
            self,
            selector: #selector(showSettings),
            name: showNotification,
            object: nil
        )
        let model = AppModel()
        model.bootstrap()
        self.model = model
        let statusBar = StatusBarController(model: model)
        self.statusBar = statusBar
        statusBar.showSettings()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        statusBar?.showSettings()
        return true
    }

    @objc private func showSettings() {
        statusBar?.showSettings()
    }
}
