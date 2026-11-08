import AppKit

@MainActor
final class StatusBarController: NSObject, NSMenuDelegate {
    private let model: AppModel
    private let settings = SettingsController()
    private let item: NSStatusItem

    init(model: AppModel) {
        self.model = model
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        if let button = item.button {
            let symbol = NSImage(systemSymbolName: "character.bubble", accessibilityDescription: "螢幕翻譯")
            let image = symbol?.withSymbolConfiguration(.init(pointSize: 15, weight: .medium)) ?? symbol
            image?.isTemplate = true
            image?.size = NSSize(width: 18, height: 18)
            button.image = image
        }
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        rebuildMenu()
    }

    @objc nonisolated func menuWillOpen(_ menu: NSMenu) {
        performSelector(onMainThread: #selector(rebuildMenu), with: nil, waitUntilDone: true)
    }

    @objc private func rebuildMenu() {
        guard let menu = item.menu else { return }
        menu.removeAllItems()
        let status = NSMenuItem(title: model.status, action: nil, keyEquivalent: "")
        status.isEnabled = false
        menu.addItem(status)
        let region = NSMenuItem(title: model.regionDescription, action: nil, keyEquivalent: "")
        region.isEnabled = false
        menu.addItem(region)
        menu.addItem(.separator())

        let toggle = NSMenuItem(
            title: model.isRunning ? "暫停" : "開始",
            action: #selector(toggleRunning),
            keyEquivalent: ""
        )
        toggle.target = self
        menu.addItem(toggle)

        let select = NSMenuItem(title: "圈選區域", action: #selector(selectRegion), keyEquivalent: "")
        select.target = self
        menu.addItem(select)

        let settingsItem = NSMenuItem(title: "設定…", action: #selector(showSettings), keyEquivalent: ",")
        settingsItem.target = self
        menu.addItem(settingsItem)
        menu.addItem(.separator())

        let quit = NSMenuItem(title: "結束", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)
    }

    @objc private func toggleRunning() {
        model.toggleRunning()
    }

    @objc private func selectRegion() {
        model.selectRegion()
    }

    @objc func showSettings() {
        settings.show(model: model)
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
