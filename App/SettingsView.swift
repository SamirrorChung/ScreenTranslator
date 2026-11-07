import AppKit
import SwiftUI
import TranslatorCore

struct SettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Picker("來源語言", selection: $model.sourceLanguage) {
                Text("自動（日文優先）").tag(SourceLanguage.auto)
                Text("日文").tag(SourceLanguage.japanese)
                Text("英文").tag(SourceLanguage.english)
                Text("簡體中文").tag(SourceLanguage.simplifiedChinese)
                Text("繁體中文").tag(SourceLanguage.traditionalChinese)
            }
            .pickerStyle(.menu)

            TextField("模型", text: $model.model)
            TextField("Ollama 位址", text: $model.ollamaURL)
            Text("目標語言是繁體中文。模型需已存在於 Ollama，例如 qwen2.5:7b。")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            HStack {
                Text("遮罩透明度")
                Slider(value: $model.opacity, in: 0.2...1.0)
                Text("\(Int((model.opacity * 100).rounded()))%")
                    .monospacedDigit()
                    .frame(width: 44, alignment: .trailing)
            }

            Text(model.regionDescription)
                .foregroundStyle(.secondary)
            Text(model.status)

            if !model.hasScreenPermission {
                Text("需要螢幕錄製權限。到「系統設定 › 隱私權與安全性 › 螢幕錄製」允許「螢幕翻譯」。允許後再按一次開始。")
                    .font(.callout)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack {
                Button("圈選區域") { model.selectRegion() }
                Button(model.isRunning ? "暫停" : "開始") { model.toggleRunning() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 460, height: 360, alignment: .top)
        .onChange(of: model.sourceLanguage) { _, _ in
            model.persistAndApply(refreshTranslation: true)
        }
        .onChange(of: model.model) { _, _ in
            model.persistAndApply(refreshTranslation: true)
        }
        .onChange(of: model.ollamaURL) { _, _ in
            model.persistAndApply(refreshTranslation: true)
        }
        .onChange(of: model.opacity) { _, _ in
            model.persistAndApply(refreshTranslation: false)
        }
    }
}

@MainActor
final class SettingsController {
    private var window: NSWindow?

    func show(model: AppModel) {
        if window == nil {
            let hosting = NSHostingController(rootView: SettingsView(model: model))
            let window = NSWindow(contentViewController: hosting)
            window.title = "螢幕翻譯"
            window.styleMask = [.titled, .closable]
            window.level = .floating
            window.collectionBehavior = [.moveToActiveSpace]
            window.isReleasedWhenClosed = false
            window.isRestorable = false
            hosting.sizingOptions = []
            hosting.preferredContentSize = NSSize(width: 460, height: 360)
            self.window = window
        }
        guard let window else { return }
        window.makeKeyAndOrderFront(nil)
        placeOnScreen(window)
        NSApp.activate()
        DispatchQueue.main.async {
            self.placeOnScreen(window)
        }
    }

    private func placeOnScreen(_ window: NSWindow) {
        let size = NSSize(width: 460, height: 380)
        let screen = window.screen ?? NSScreen.main ?? NSScreen.screens.first
        let visible = screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1280, height: 800)
        let frame = NSRect(
            x: visible.midX - size.width / 2,
            y: visible.midY - size.height / 2,
            width: size.width,
            height: size.height
        )
        window.setFrame(frame, display: true)
    }
}
