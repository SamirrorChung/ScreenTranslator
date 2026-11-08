import Foundation
import Testing
@testable import TranslatorCore

actor ScriptedTranslator: TextTranslating {
    private var results: [Result<String, TranslationError>]
    private(set) var calls = 0
    private(set) var lastInput = ""

    init(results: [Result<String, TranslationError>]) {
        self.results = results
    }

    func translate(
        _ text: String,
        sourceLanguage: SourceLanguage,
        model: String,
        endpoint: URL
    ) async throws -> String {
        calls += 1
        lastInput = text
        guard !results.isEmpty else { throw TranslationError.emptyResponse }
        return try results.removeFirst().get()
    }
}

private func line(
    _ text: String,
    x: Double = 0,
    y: Double = 0,
    width: Double = 80,
    height: Double = 20,
    confidence: Double = 0.9
) -> RecognizedLine {
    RecognizedLine(text: text, x: x, y: y, width: width, height: height, confidence: confidence)
}

@Test func emptyFrameStaysTransparent() async {
    let pipeline = TranslationPipeline(config: .default, translator: ScriptedTranslator(results: []))
    let update = await pipeline.update(lines: [], now: Date(timeIntervalSince1970: 0))
    #expect(update.command == .clear)
    #expect(update.note == .none)
}

@Test func lowConfidenceIsTreatedAsNoText() async {
    let translator = ScriptedTranslator(results: [.success("不該出現")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let update = await pipeline.update(
        lines: [line("こんにちは", confidence: 0.05)],
        now: Date(timeIntervalSince1970: 0)
    )
    #expect(update.command == .clear)
    #expect(await translator.calls == 0)
}

@Test func translatesThenReusesCacheWhenTheLineMoves() async {
    let translator = ScriptedTranslator(results: [.success("你好"), .success("不該用到")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let now = Date(timeIntervalSince1970: 10)
    let first = await pipeline.update(lines: [line("こんにちは", x: 4)], now: now)
    let second = await pipeline.update(lines: [line("こんにちは", x: 40)], now: now)

    guard case .show(let firstMasks) = first.command else {
        Issue.record("first frame should show a mask")
        return
    }
    guard case .show(let secondMasks) = second.command else {
        Issue.record("moved line should still show a mask")
        return
    }
    #expect(first.note == .translated)
    #expect(second.note == .cached)
    #expect(firstMasks[0].text == "你好")
    #expect(firstMasks[0].x == 4)
    #expect(secondMasks[0].x == 40)
    #expect(secondMasks[0].text == "你好")
    #expect(await translator.calls == 1)
}

@Test func whitespaceDifferencesDoNotRetranslate() async {
    let translator = ScriptedTranslator(results: [.success("你好")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let now = Date(timeIntervalSince1970: 3)
    _ = await pipeline.update(lines: [line("こんにちは")], now: now)
    let again = await pipeline.update(lines: [line("  こんにちは　 ")], now: now)
    #expect(again.note == .cached)
    #expect(await translator.calls == 1)
}

@Test func emptyTextHoldsThenClears() async {
    let translator = ScriptedTranslator(results: [.success("你好")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let start = Date(timeIntervalSince1970: 20)
    _ = await pipeline.update(lines: [line("こんにちは")], now: start)

    let immediate = await pipeline.update(lines: [], now: start)
    let stillHolding = await pipeline.update(lines: [], now: start.addingTimeInterval(0.39))
    let cleared = await pipeline.update(lines: [], now: start.addingTimeInterval(0.41))

    #expect(immediate.command == .keep)
    #expect(immediate.note == .waitingToClear)
    #expect(stillHolding.command == .keep)
    #expect(cleared.command == .clear)
}

@Test func textReturningDuringHoldDoesNotClear() async {
    let translator = ScriptedTranslator(results: [.success("你好")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let start = Date(timeIntervalSince1970: 30)
    _ = await pipeline.update(lines: [line("こんにちは")], now: start)
    _ = await pipeline.update(lines: [], now: start)
    let back = await pipeline.update(lines: [line("こんにちは")], now: start.addingTimeInterval(0.2))
    #expect(back.note == .cached)
    if case .show(let masks) = back.command {
        #expect(masks[0].text == "你好")
    } else {
        Issue.record("text should come back with a mask")
    }
}

@Test func failedTranslationKeepsPreviousMask() async {
    let translator = ScriptedTranslator(results: [
        .success("你好"),
        .failure(.unreachable),
    ])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let now = Date(timeIntervalSince1970: 40)
    _ = await pipeline.update(lines: [line("こんにちは")], now: now)
    let failed = await pipeline.update(lines: [line("さようなら")], now: now)
    #expect(failed.command == .keep)
    #expect(failed.note == .failed("無法連上 Ollama"))
}

@Test func failedFirstTranslationStaysTransparent() async {
    let translator = ScriptedTranslator(results: [.failure(.unreachable)])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let failed = await pipeline.update(
        lines: [line("こんにちは")],
        now: Date(timeIntervalSince1970: 50)
    )
    #expect(failed.command == .clear)
    #expect(failed.note == .failed("無法連上 Ollama"))
}

@Test func mismatchedLineCountUsesOneMask() async {
    let translator = ScriptedTranslator(results: [.success("合併的譯文")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let update = await pipeline.update(
        lines: [
            line("あ", x: 10, y: 5, width: 20, height: 10),
            line("い", x: 40, y: 5, width: 20, height: 10),
        ],
        now: Date(timeIntervalSince1970: 60)
    )
    guard case .show(let masks) = update.command else {
        Issue.record("expected a single combined mask")
        return
    }
    #expect(masks.count == 1)
    #expect(masks[0].text == "合併的譯文")
    #expect(masks[0].x == 10)
    #expect(masks[0].width == 50)
    #expect(await translator.lastInput == "あ\nい")
}

@Test func matchingLinesKeepSeparateMasks() async {
    let translator = ScriptedTranslator(results: [.success("甲\n乙\n")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    let update = await pipeline.update(
        lines: [line("a", y: 0), line("b", y: 30)],
        now: Date(timeIntervalSince1970: 70)
    )
    guard case .show(let masks) = update.command else {
        Issue.record("expected one mask per line")
        return
    }
    #expect(masks.map(\.text) == ["甲", "乙"])
}

@Test func opacityChangeDoesNotRetranslate() async {
    let translator = ScriptedTranslator(results: [.success("你好")])
    let pipeline = TranslationPipeline(config: .default, translator: translator)
    _ = await pipeline.update(lines: [line("こんにちは")], now: Date(timeIntervalSince1970: 80))
    var faded = TranslatorConfig.default
    faded.opacity = 0.4
    let command = await pipeline.replaceConfig(faded)
    guard case .show(let masks) = command else {
        Issue.record("opacity change should redraw the current mask")
        return
    }
    #expect(masks[0].opacity == 0.4)
    #expect(masks[0].text == "你好")
    #expect(await translator.calls == 1)
}

@Test func cacheDropsTheOldestSentence() async {
    let translator = ScriptedTranslator(results: [
        .success("一"),
        .success("二"),
        .success("一再譯"),
    ])
    let pipeline = TranslationPipeline(config: .default, translator: translator, cacheLimit: 1)
    let now = Date(timeIntervalSince1970: 90)
    _ = await pipeline.update(lines: [line("a")], now: now)
    _ = await pipeline.update(lines: [line("b")], now: now)
    let again = await pipeline.update(lines: [line("a")], now: now)
    #expect(again.note == .translated)
    if case .show(let masks) = again.command {
        #expect(masks[0].text == "一再譯")
    }
    #expect(await translator.calls == 3)
}

@Test func promptAsksForTraditionalChinese() {
    let prompt = TranslationText.systemPrompt(for: .japanese)
    #expect(prompt.contains("繁體中文"))
    #expect(prompt.contains("日文"))
}

@Test func cleanDropsFencesAndLabels() {
    #expect(TranslationText.clean("```\n你好\n```") == "你好")
    #expect(TranslationText.clean("「你好」") == "你好")
    #expect(TranslationText.clean("譯文：你好") == "你好")
}
