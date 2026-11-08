import Foundation

public actor TranslationPipeline {
    private var config: TranslatorConfig
    private let translator: any TextTranslating
    private var cache: TranslationCache
    private var lastKey: String?
    private var lastTranslation: String?
    private var lastMasks: [TextMask] = []
    private var emptySince: Date?
    private var generation = 0

    public init(config: TranslatorConfig, cacheLimit: Int = 100) {
        self.init(config: config, translator: OllamaClient(), cacheLimit: cacheLimit)
    }

    public init(config: TranslatorConfig, translator: any TextTranslating, cacheLimit: Int = 100) {
        self.config = config
        self.translator = translator
        self.cache = TranslationCache(limit: cacheLimit)
    }

    public func reset() {
        generation += 1
        lastKey = nil
        lastTranslation = nil
        lastMasks = []
        emptySince = nil
    }

    /// Updates opacity on masks already on screen. Cached sentences stay cached.
    public func replaceConfig(_ newConfig: TranslatorConfig) -> OverlayCommand {
        config = newConfig
        guard !lastMasks.isEmpty else { return .clear }
        lastMasks = lastMasks.map { mask in
            var copy = mask
            copy.opacity = config.opacity
            return copy
        }
        return .show(lastMasks)
    }

    public func update(lines: [RecognizedLine], now: Date = Date()) async -> PipelineUpdate {
        let accepted = usableLines(lines)
        if accepted.isEmpty {
            return clearOrHold(now: now)
        }

        emptySince = nil
        generation += 1
        let token = generation
        let ordered = accepted.sorted(by: comesBefore)
        let signature = ordered.map { TranslationText.normalizeLine($0.text) }.joined(separator: "\n")
        let key = cacheKey(for: signature)

        if key == lastKey, let lastTranslation {
            let masks = makeMasks(lines: ordered, translation: lastTranslation)
            lastMasks = masks
            return PipelineUpdate(command: masks.isEmpty ? .clear : .show(masks), note: .cached)
        }
        if let cached = cache.value(for: key) {
            lastKey = key
            lastTranslation = cached
            let masks = makeMasks(lines: ordered, translation: cached)
            lastMasks = masks
            return PipelineUpdate(command: masks.isEmpty ? .clear : .show(masks), note: .cached)
        }

        do {
            let translated = try await translator.translate(
                signature,
                sourceLanguage: config.sourceLanguage,
                model: config.model,
                endpoint: config.baseURL
            )
            guard token == generation else {
                return PipelineUpdate(command: .keep, note: .none)
            }
            let cleaned = TranslationText.clean(translated)
            guard !cleaned.isEmpty else {
                return fail("Ollama 沒有回傳譯文")
            }
            cache.insert(cleaned, for: key)
            lastKey = key
            lastTranslation = cleaned
            let masks = makeMasks(lines: ordered, translation: cleaned)
            lastMasks = masks
            if masks.isEmpty {
                return PipelineUpdate(command: .clear, note: .none)
            }
            return PipelineUpdate(command: .show(masks), note: .translated)
        } catch {
            guard token == generation else {
                return PipelineUpdate(command: .keep, note: .none)
            }
            let message = (error as? TranslationError)?.description ?? "翻譯失敗"
            return fail(message)
        }
    }

    private func usableLines(_ lines: [RecognizedLine]) -> [RecognizedLine] {
        lines.filter { line in
            line.confidence >= config.minimumConfidence
                && !TranslationText.normalizeLine(line.text).isEmpty
        }
    }

    private func comesBefore(_ lhs: RecognizedLine, _ rhs: RecognizedLine) -> Bool {
        if lhs.y != rhs.y { return lhs.y < rhs.y }
        return lhs.x < rhs.x
    }

    private func cacheKey(for signature: String) -> String {
        "\(config.baseURL.absoluteString)|\(config.model)|\(config.sourceLanguage.rawValue)|\(signature)"
    }

    private func clearOrHold(now: Date) -> PipelineUpdate {
        guard !lastMasks.isEmpty else {
            return PipelineUpdate(command: .clear, note: .none)
        }
        if emptySince == nil {
            emptySince = now
        }
        if let emptySince, now.timeIntervalSince(emptySince) >= config.emptyHold {
            lastMasks = []
            lastKey = nil
            lastTranslation = nil
            self.emptySince = nil
            generation += 1
            return PipelineUpdate(command: .clear, note: .none)
        }
        return PipelineUpdate(command: .keep, note: .waitingToClear)
    }

    private func fail(_ message: String) -> PipelineUpdate {
        if lastMasks.isEmpty {
            return PipelineUpdate(command: .clear, note: .failed(message))
        }
        return PipelineUpdate(command: .keep, note: .failed(message))
    }

    private func makeMasks(lines: [RecognizedLine], translation: String) -> [TextMask] {
        var parts = translation.components(separatedBy: "\n")
        if parts.last?.trimmingCharacters(in: .whitespaces).isEmpty == true {
            parts.removeLast()
        }
        if parts.count == lines.count {
            return zip(lines, parts).compactMap { line, raw in
                let text = raw.trimmingCharacters(in: .whitespaces)
                guard !text.isEmpty else { return nil }
                return TextMask(
                    x: line.x,
                    y: line.y,
                    width: line.width,
                    height: line.height,
                    text: text,
                    opacity: config.opacity
                )
            }
        }
        let text = translation.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty, let box = union(of: lines) else { return [] }
        return [
            TextMask(
                x: box.x,
                y: box.y,
                width: box.width,
                height: box.height,
                text: text,
                opacity: config.opacity
            )
        ]
    }

    private func union(of lines: [RecognizedLine]) -> (x: Double, y: Double, width: Double, height: Double)? {
        guard let first = lines.first else { return nil }
        var minX = first.x
        var minY = first.y
        var maxX = first.x + first.width
        var maxY = first.y + first.height
        for line in lines.dropFirst() {
            minX = min(minX, line.x)
            minY = min(minY, line.y)
            maxX = max(maxX, line.x + line.width)
            maxY = max(maxY, line.y + line.height)
        }
        return (minX, minY, maxX - minX, maxY - minY)
    }
}
