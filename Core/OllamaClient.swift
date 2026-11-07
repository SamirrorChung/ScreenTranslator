import Foundation

public enum TranslationError: Error, Equatable, Sendable, CustomStringConvertible {
    case invalidEndpoint
    case emptySource
    case badStatus(Int)
    case emptyResponse
    case unreachable

    public var description: String {
        switch self {
        case .invalidEndpoint:
            "Ollama 位址不正確"
        case .emptySource:
            "沒有可翻譯的文字"
        case .badStatus(let code):
            "Ollama 回應錯誤（\(code)）"
        case .emptyResponse:
            "Ollama 沒有回傳譯文"
        case .unreachable:
            "無法連上 Ollama"
        }
    }
}

enum TranslationText {
    static func systemPrompt(for source: SourceLanguage) -> String {
        let hint: String
        switch source {
        case .auto:
            hint = "先自行判斷來源語言。"
        case .japanese:
            hint = "來源語言是日文。"
        case .english:
            hint = "來源語言是英文。"
        case .simplifiedChinese:
            hint = "來源語言是簡體中文，請轉成繁體中文。"
        case .traditionalChinese:
            hint = "來源語言已是繁體中文，請整理成自然的繁體中文。"
        }
        return """
        你是螢幕字幕翻譯。把使用者的文字譯成繁體中文。
        \(hint)
        只輸出譯文。不要解釋、不要加引號、不要保留原文。
        輸入有幾行，輸出就有幾行，順序相同。
        """
    }

    static func normalizeLine(_ text: String) -> String {
        let collapsed = text.replacingOccurrences(
            of: "[ \\t\\u{3000}]+",
            with: " ",
            options: .regularExpression
        )
        return collapsed.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func clean(_ raw: String) -> String {
        var text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.hasPrefix("```") {
            var lines = text.components(separatedBy: "\n")
            if lines.first?.hasPrefix("```") == true {
                lines.removeFirst()
            }
            if lines.last?.trimmingCharacters(in: .whitespaces) == "```" {
                lines.removeLast()
            }
            text = lines.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let wrappers = [("\"", "\""), ("「", "」"), ("『", "』")]
        for (open, close) in wrappers where text.hasPrefix(open) && text.hasSuffix(close) && text.count > open.count + close.count {
            text = String(text.dropFirst(open.count).dropLast(close.count))
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }
        for prefix in ["譯文：", "譯文:", "翻譯：", "翻譯:"] where text.hasPrefix(prefix) {
            text = String(text.dropFirst(prefix.count)).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return text
    }
}

public struct OllamaClient: TextTranslating {
    private let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func translate(
        _ text: String,
        sourceLanguage: SourceLanguage,
        model: String,
        endpoint: URL
    ) async throws -> String {
        let source = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !source.isEmpty else { throw TranslationError.emptySource }

        let url = endpoint.appendingPathComponent("api/chat")
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 20
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body = ChatRequest(
            model: model,
            stream: false,
            keepAlive: "30m",
            messages: [
                .init(role: "system", content: TranslationText.systemPrompt(for: sourceLanguage)),
                .init(role: "user", content: source),
            ],
            options: .init(temperature: 0.1, numPredict: 256)
        )
        request.httpBody = try JSONEncoder().encode(body)

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw TranslationError.unreachable
        }
        guard let http = response as? HTTPURLResponse else {
            throw TranslationError.unreachable
        }
        guard http.statusCode == 200 else {
            throw TranslationError.badStatus(http.statusCode)
        }
        let decoded = try JSONDecoder().decode(ChatResponse.self, from: data)
        let cleaned = TranslationText.clean(decoded.message.content)
        guard !cleaned.isEmpty else { throw TranslationError.emptyResponse }
        return cleaned
    }
}

private struct ChatRequest: Encodable {
    var model: String
    var stream: Bool
    var keepAlive: String
    var messages: [Message]
    var options: Options

    struct Message: Encodable {
        var role: String
        var content: String
    }

    struct Options: Encodable {
        var temperature: Double
        var numPredict: Int
    }

    enum CodingKeys: String, CodingKey {
        case model
        case stream
        case keepAlive = "keep_alive"
        case messages
        case options
    }
}

private struct ChatResponse: Decodable {
    struct Message: Decodable {
        var content: String
    }

    var message: Message
}

private extension ChatRequest.Options {
    enum CodingKeys: String, CodingKey {
        case temperature
        case numPredict = "num_predict"
    }
}
