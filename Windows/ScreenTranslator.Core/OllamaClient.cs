using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScreenTranslator;

public sealed class TranslationException : Exception
{
    private TranslationException(string message) : base(message)
    {
    }

    public static TranslationException InvalidEndpoint() => new("Ollama 位址不正確");
    public static TranslationException EmptySource() => new("沒有可翻譯的文字");
    public static TranslationException BadStatus(int code) => new($"Ollama 回應錯誤（{code}）");
    public static TranslationException EmptyResponse() => new("Ollama 沒有回傳譯文");
    public static TranslationException Unreachable() => new("無法連上 Ollama");
}

public static class TranslationText
{
    public static string SystemPrompt(SourceLanguage source)
    {
        var hint = source switch
        {
            SourceLanguage.Japanese => "來源語言是日文。",
            SourceLanguage.English => "來源語言是英文。",
            SourceLanguage.SimplifiedChinese => "來源語言是簡體中文，請轉成繁體中文。",
            SourceLanguage.TraditionalChinese => "來源語言已是繁體中文，請整理成自然的繁體中文。",
            _ => "先自行判斷來源語言。",
        };
        return $"""
            你是螢幕字幕翻譯。把使用者的文字譯成繁體中文。
            {hint}
            只輸出譯文。不要解釋、不要加引號、不要保留原文。
            輸入有幾行，輸出就有幾行，順序相同。
            """;
    }

    public static string NormalizeLine(string text)
    {
        var collapsed = WhitespacePattern.Replace(text, " ");
        return collapsed.Trim();
    }

    public static string Clean(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = text.Split('\n').ToList();
            if (lines.Count > 0 && lines[0].StartsWith("```", StringComparison.Ordinal))
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[^1].Trim() == "```")
                lines.RemoveAt(lines.Count - 1);
            text = string.Join("\n", lines).Trim();
        }

        (string Open, string Close)[] wrappers = [("\"", "\""), ("「", "」"), ("『", "』")];
        foreach (var (open, close) in wrappers)
        {
            if (text.StartsWith(open, StringComparison.Ordinal) &&
                text.EndsWith(close, StringComparison.Ordinal) &&
                text.Length > open.Length + close.Length)
            {
                text = text.Substring(open.Length, text.Length - open.Length - close.Length).Trim();
            }
        }

        foreach (var prefix in new[] { "譯文：", "譯文:", "翻譯：", "翻譯:" })
        {
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            text = text.Substring(prefix.Length).Trim();
        }

        return text;
    }

    private static readonly Regex WhitespacePattern = new(@"[ \t\u3000]+", RegexOptions.Compiled);
}

public sealed class OllamaClient : ITextTranslating
{
    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<string> Translate(string text, SourceLanguage sourceLanguage, string model, Uri endpoint)
    {
        var source = text.Trim();
        if (source.Length == 0) throw TranslationException.EmptySource();

        var url = ChatUri(endpoint);
        var body = new ChatRequest
        {
            Model = model,
            Stream = false,
            KeepAlive = "30m",
            Messages =
            [
                new ChatMessage { Role = "system", Content = TranslationText.SystemPrompt(sourceLanguage) },
                new ChatMessage { Role = "user", Content = source },
            ],
            Options = new ChatOptions { Temperature = 0.1, NumPredict = 256 },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await Http.SendAsync(request, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw TranslationException.BadStatus((int)response.StatusCode);
            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            var decoded = JsonSerializer.Deserialize<ChatResponse>(payload)
                ?? throw TranslationException.EmptyResponse();
            var content = decoded.Message?.Content;
            if (string.IsNullOrEmpty(content)) throw TranslationException.EmptyResponse();
            var cleaned = TranslationText.Clean(content);
            if (cleaned.Length == 0) throw TranslationException.EmptyResponse();
            return cleaned;
        }
        catch (TranslationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception)
        {
            throw TranslationException.Unreachable();
        }
    }

    private static Uri ChatUri(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw TranslationException.InvalidEndpoint();
        }

        var builder = new UriBuilder(endpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = path + "/api/chat";
        return builder.Uri;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("keep_alive")]
        public required string KeepAlive { get; init; }

        [JsonPropertyName("messages")]
        public required IReadOnlyList<ChatMessage> Messages { get; init; }

        [JsonPropertyName("options")]
        public required ChatOptions Options { get; init; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class ChatOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }
    }
}
