import Foundation

public enum SourceLanguage: String, Codable, Sendable, CaseIterable {
    case auto
    case japanese
    case english
    case simplifiedChinese
    case traditionalChinese

    /// Vision / BCP-47 tags. Automatic mode lists Japanese first.
    public var recognitionLanguageTags: [String] {
        switch self {
        case .auto:
            ["ja-JP", "en-US", "zh-Hant", "zh-Hans"]
        case .japanese:
            ["ja-JP"]
        case .english:
            ["en-US"]
        case .simplifiedChinese:
            ["zh-Hans"]
        case .traditionalChinese:
            ["zh-Hant"]
        }
    }

    public var detectsLanguage: Bool {
        self == .auto
    }
}

public struct RecognizedLine: Equatable, Sendable {
    public var text: String
    public var x: Double
    public var y: Double
    public var width: Double
    public var height: Double
    public var confidence: Double

    public init(
        text: String,
        x: Double,
        y: Double,
        width: Double,
        height: Double,
        confidence: Double
    ) {
        self.text = text
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.confidence = confidence
    }
}

public struct TextMask: Equatable, Sendable {
    public var x: Double
    public var y: Double
    public var width: Double
    public var height: Double
    public var text: String
    public var opacity: Double

    public init(
        x: Double,
        y: Double,
        width: Double,
        height: Double,
        text: String,
        opacity: Double
    ) {
        self.x = x
        self.y = y
        self.width = width
        self.height = height
        self.text = text
        self.opacity = opacity
    }
}

public enum OverlayCommand: Equatable, Sendable {
    case keep
    case clear
    case show([TextMask])
}

public enum PipelineNote: Equatable, Sendable {
    case none
    case translated
    case cached
    case waitingToClear
    case failed(String)
}

public struct PipelineUpdate: Equatable, Sendable {
    public var command: OverlayCommand
    public var note: PipelineNote

    public init(command: OverlayCommand, note: PipelineNote) {
        self.command = command
        self.note = note
    }
}

public struct TranslatorConfig: Equatable, Sendable {
    public var baseURL: URL
    public var model: String
    public var sourceLanguage: SourceLanguage
    public var opacity: Double
    public var minimumConfidence: Double
    public var emptyHold: TimeInterval

    public init(
        baseURL: URL,
        model: String,
        sourceLanguage: SourceLanguage,
        opacity: Double,
        minimumConfidence: Double = 0.2,
        emptyHold: TimeInterval = 0.4
    ) {
        self.baseURL = baseURL
        self.model = model
        self.sourceLanguage = sourceLanguage
        self.opacity = min(1, max(0, opacity))
        self.minimumConfidence = minimumConfidence
        self.emptyHold = emptyHold
    }

    public static let `default` = TranslatorConfig(
        baseURL: URL(string: "http://127.0.0.1:11434")!,
        model: "qwen2.5:7b",
        sourceLanguage: .auto,
        opacity: 0.75
    )
}

public protocol TextTranslating: Sendable {
    func translate(
        _ text: String,
        sourceLanguage: SourceLanguage,
        model: String,
        endpoint: URL
    ) async throws -> String
}
