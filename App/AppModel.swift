import AppKit
import TranslatorCore

struct LoopSnapshot: Sendable {
    var isRunning: Bool
    var region: CaptureRegion?
    var languageTags: [String]
    var detectsLanguage: Bool
    var windowID: UInt32?
    var forceRefresh: Bool
    var pipeline: TranslationPipeline
}

@MainActor
final class AppModel: ObservableObject {
    @Published var sourceLanguage: SourceLanguage
    @Published var model: String
    @Published var ollamaURL: String
    @Published var opacity: Double
    @Published var isRunning = false
    @Published var status = "尚未開始"
    @Published var hasScreenPermission = false
    @Published var regionDescription = "尚未圈選"

    private let pipeline: TranslationPipeline
    private let selector = RegionSelector()
    private let defaults = UserDefaults.standard
    private var region: CaptureRegion?
    private var overlay: OverlayPanel?
    private var loopTask: Task<Void, Never>?
    private var forceRefresh = false
    private var lastPixelScale: Double = 2

    init() {
        let storedLanguage = defaults.string(forKey: Keys.sourceLanguage)
            .flatMap(SourceLanguage.init(rawValue:)) ?? .auto
        let storedModel = defaults.string(forKey: Keys.model) ?? TranslatorConfig.default.model
        let storedURL = defaults.string(forKey: Keys.ollamaURL) ?? TranslatorConfig.default.baseURL.absoluteString
        let storedOpacity = (defaults.object(forKey: Keys.opacity) as? Double) ?? TranslatorConfig.default.opacity
        sourceLanguage = storedLanguage
        model = storedModel
        ollamaURL = storedURL
        opacity = storedOpacity
        if let data = defaults.data(forKey: Keys.region),
           let stored = try? JSONDecoder().decode(CaptureRegion.self, from: data) {
            region = stored
            regionDescription = stored.summary
        }
        pipeline = TranslationPipeline(config: Self.makeConfig(
            sourceLanguage: storedLanguage,
            model: storedModel,
            ollamaURL: storedURL,
            opacity: storedOpacity
        ) ?? .default)
    }

    func bootstrap() {
        hasScreenPermission = CGPreflightScreenCaptureAccess()
    }

    func toggleRunning() {
        if isRunning {
            pause()
        } else {
            start()
        }
    }

    func selectRegion() {
        let resume = isRunning
        if isRunning {
            pause(updateStatus: false)
        }
        status = "圈選範圍中…"
        selector.begin(
            onSelect: { [weak self] region in
                guard let self else { return }
                self.region = region
                self.regionDescription = region.summary
                self.saveRegion()
                self.status = "已圈選範圍"
                if resume {
                    self.start()
                }
            },
            onCancel: { [weak self] in
                guard let self else { return }
                self.status = "已取消圈選"
                if resume {
                    self.start()
                }
            }
        )
    }

    func persistAndApply(refreshTranslation: Bool) {
        saveSettings()
        guard let config = currentConfig() else {
            status = "請檢查模型名稱與 Ollama 位址"
            return
        }
        if refreshTranslation {
            forceRefresh = true
        }
        Task {
            let command = await pipeline.replaceConfig(config)
            guard isRunning, !refreshTranslation else { return }
            let note: PipelineNote = if case .show = command { .cached } else { .none }
            apply(PipelineUpdate(command: command, note: note), pixelScale: lastPixelScale)
        }
    }

    func consumeSnapshot() -> LoopSnapshot {
        let refresh = forceRefresh
        forceRefresh = false
        return LoopSnapshot(
            isRunning: isRunning,
            region: region,
            languageTags: sourceLanguage.recognitionLanguageTags,
            detectsLanguage: sourceLanguage.detectsLanguage,
            windowID: overlay?.windowID,
            forceRefresh: refresh,
            pipeline: pipeline
        )
    }

    func apply(_ update: PipelineUpdate, pixelScale: Double) {
        guard isRunning else { return }
        lastPixelScale = pixelScale
        switch update.command {
        case .keep:
            break
        case .clear:
            overlay?.setMasks([], pixelScale: pixelScale)
        case .show(let masks):
            overlay?.setMasks(masks, pixelScale: pixelScale)
        }
        switch update.note {
        case .none:
            if case .clear = update.command {
                status = "目前沒有文字"
            }
        case .translated, .cached:
            status = "翻譯中"
        case .waitingToClear:
            break
        case .failed(let message):
            if case .keep = update.command {
                status = "\(message)，保留上一句"
            } else {
                status = message
            }
        }
    }

    func noteCaptureFailure(_ message: String) {
        let text = "擷取失敗：\(message)"
        if status != text {
            status = text
        }
    }

    func noteCaptureHealthy() {
        if status.hasPrefix("擷取失敗") {
            status = "翻譯中"
        }
    }

    private func start() {
        guard !isRunning else { return }
        guard let region else {
            status = "請先圈選區域"
            return
        }
        guard ScreenIdentity.screen(for: CGDirectDisplayID(region.displayID)) != nil else {
            status = "找不到當初圈選的螢幕"
            return
        }
        guard ensurePermission() else { return }
        guard currentConfig() != nil else {
            status = "請檢查模型名稱與 Ollama 位址"
            return
        }
        isRunning = true
        status = "翻譯中"
        showOverlay(for: region)
        let pipeline = self.pipeline
        loopTask = Task.detached { [weak self] in
            await pipeline.reset()
            guard let self else { return }
            await runCaptureLoop(model: self)
        }
    }

    private func pause(updateStatus: Bool = true) {
        isRunning = false
        loopTask?.cancel()
        loopTask = nil
        overlay?.close()
        overlay = nil
        let pipeline = self.pipeline
        Task { await pipeline.reset() }
        if updateStatus {
            status = "已暫停"
        }
    }

    private func showOverlay(for region: CaptureRegion) {
        guard let screen = ScreenIdentity.screen(for: CGDirectDisplayID(region.displayID)) else { return }
        let panel = OverlayPanel(frame: region.appKitFrame(on: screen))
        panel.orderFront()
        overlay = panel
    }

    private func ensurePermission() -> Bool {
        if CGPreflightScreenCaptureAccess() {
            hasScreenPermission = true
            return true
        }
        hasScreenPermission = CGRequestScreenCaptureAccess()
        if !hasScreenPermission {
            status = "需要螢幕錄製權限"
        }
        return hasScreenPermission
    }

    private func currentConfig() -> TranslatorConfig? {
        Self.makeConfig(
            sourceLanguage: sourceLanguage,
            model: model,
            ollamaURL: ollamaURL,
            opacity: opacity
        )
    }

    private static func makeConfig(
        sourceLanguage: SourceLanguage,
        model: String,
        ollamaURL: String,
        opacity: Double
    ) -> TranslatorConfig? {
        let trimmedURL = ollamaURL.trimmingCharacters(in: .whitespacesAndNewlines)
        let trimmedModel = model.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedModel.isEmpty,
              let url = URL(string: trimmedURL),
              let scheme = url.scheme,
              scheme == "http" || scheme == "https",
              url.host != nil
        else {
            return nil
        }
        return TranslatorConfig(
            baseURL: url,
            model: trimmedModel,
            sourceLanguage: sourceLanguage,
            opacity: opacity
        )
    }

    private func saveSettings() {
        defaults.set(sourceLanguage.rawValue, forKey: Keys.sourceLanguage)
        defaults.set(model, forKey: Keys.model)
        defaults.set(ollamaURL, forKey: Keys.ollamaURL)
        defaults.set(opacity, forKey: Keys.opacity)
    }

    private func saveRegion() {
        guard let region, let data = try? JSONEncoder().encode(region) else { return }
        defaults.set(data, forKey: Keys.region)
    }
}

private enum Keys {
    static let sourceLanguage = "sourceLanguage"
    static let model = "model"
    static let ollamaURL = "ollamaURL"
    static let opacity = "opacity"
    static let region = "region"
}

private func runCaptureLoop(model: AppModel) async {
    var lastHash: UInt64?
    while !Task.isCancelled {
        let started = ContinuousClock.now
        let snapshot = await model.consumeSnapshot()
        guard snapshot.isRunning, let region = snapshot.region else { return }
        do {
            let frame = try await FrameReader.read(
                region: region,
                excludingWindowID: snapshot.windowID,
                languages: snapshot.languageTags,
                detectsLanguage: snapshot.detectsLanguage,
                previousHash: snapshot.forceRefresh ? nil : lastHash
            )
            if let hash = frame.hash {
                lastHash = hash
            }
            if frame.changed {
                let update = await snapshot.pipeline.update(lines: frame.lines, now: Date())
                await model.apply(update, pixelScale: frame.pixelScale)
            } else {
                await model.noteCaptureHealthy()
            }
        } catch {
            let message = (error as? CaptureError)?.description ?? error.localizedDescription
            await model.noteCaptureFailure(message)
        }
        let elapsed = started.duration(to: .now)
        let interval = Duration.milliseconds(250)
        if elapsed < interval {
            try? await Task.sleep(for: interval - elapsed)
        }
    }
}
