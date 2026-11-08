namespace ScreenTranslator;

public sealed record TranslationExchange(
    DateTimeOffset At,
    string Model,
    string SystemPrompt,
    string Content,
    string? Response,
    string? Error);

public static class TranslationLog
{
    private static readonly object Gate = new();
    private static TranslationExchange? _latest;

    public static event Action? Changed;

    public static TranslationExchange? Latest
    {
        get
        {
            lock (Gate) return _latest;
        }
    }

    public static void Record(TranslationExchange exchange)
    {
        lock (Gate) _latest = exchange;
        var handler = Changed;
        if (handler is null) return;
        try
        {
            handler();
        }
        catch (Exception)
        {
        }
    }
}
