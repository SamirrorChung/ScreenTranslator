namespace ScreenTranslator;

public enum SourceLanguage
{
    Auto,
    Japanese,
    English,
    SimplifiedChinese,
    TraditionalChinese,
}

public static class SourceLanguages
{
    public static string RawValue(this SourceLanguage language) => language switch
    {
        SourceLanguage.Japanese => "japanese",
        SourceLanguage.English => "english",
        SourceLanguage.SimplifiedChinese => "simplifiedChinese",
        SourceLanguage.TraditionalChinese => "traditionalChinese",
        _ => "auto",
    };

    public static SourceLanguage Parse(string? raw) => raw switch
    {
        "japanese" => SourceLanguage.Japanese,
        "english" => SourceLanguage.English,
        "simplifiedChinese" => SourceLanguage.SimplifiedChinese,
        "traditionalChinese" => SourceLanguage.TraditionalChinese,
        _ => SourceLanguage.Auto,
    };

    /// <summary>Vision / BCP-47 tags. Automatic mode lists Japanese first.</summary>
    public static IReadOnlyList<string> RecognitionLanguageTags(this SourceLanguage language) => language switch
    {
        SourceLanguage.Japanese => ["ja-JP"],
        SourceLanguage.English => ["en-US"],
        SourceLanguage.SimplifiedChinese => ["zh-Hans"],
        SourceLanguage.TraditionalChinese => ["zh-Hant"],
        _ => ["ja-JP", "en-US", "zh-Hant", "zh-Hans"],
    };

    public static bool DetectsLanguage(this SourceLanguage language) => language == SourceLanguage.Auto;
}

public sealed record RecognizedLine(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    double Confidence);

public sealed record TextMask(
    double X,
    double Y,
    double Width,
    double Height,
    string Text,
    double Opacity);

public enum OverlayCommandKind
{
    Keep,
    Clear,
    Show,
}

public sealed record OverlayCommand(OverlayCommandKind Kind, IReadOnlyList<TextMask> Masks)
{
    public static OverlayCommand Keep { get; } = new(OverlayCommandKind.Keep, Array.Empty<TextMask>());
    public static OverlayCommand Clear { get; } = new(OverlayCommandKind.Clear, Array.Empty<TextMask>());
    public static OverlayCommand Show(IReadOnlyList<TextMask> masks) => new(OverlayCommandKind.Show, masks);
}

public enum PipelineNoteKind
{
    None,
    Translated,
    Cached,
    WaitingToClear,
    Failed,
}

public sealed record PipelineNote(PipelineNoteKind Kind, string? Message = null)
{
    public static PipelineNote None { get; } = new(PipelineNoteKind.None);
    public static PipelineNote Translated { get; } = new(PipelineNoteKind.Translated);
    public static PipelineNote Cached { get; } = new(PipelineNoteKind.Cached);
    public static PipelineNote WaitingToClear { get; } = new(PipelineNoteKind.WaitingToClear);
    public static PipelineNote Failed(string message) => new(PipelineNoteKind.Failed, message);
}

public sealed record PipelineUpdate(OverlayCommand Command, PipelineNote Note);

public sealed record TranslatorConfig
{
    public Uri BaseUrl { get; }
    public string Model { get; }
    public SourceLanguage SourceLanguage { get; }
    public double Opacity { get; }
    public double MinimumConfidence { get; }
    public TimeSpan EmptyHold { get; }

    public TranslatorConfig(
        Uri baseUrl,
        string model,
        SourceLanguage sourceLanguage,
        double opacity,
        double minimumConfidence = 0.2,
        TimeSpan? emptyHold = null)
    {
        BaseUrl = baseUrl;
        Model = model;
        SourceLanguage = sourceLanguage;
        Opacity = Math.Clamp(opacity, 0, 1);
        MinimumConfidence = minimumConfidence;
        EmptyHold = emptyHold ?? TimeSpan.FromSeconds(0.4);
    }

    public static TranslatorConfig Default { get; } = new(
        new Uri("http://127.0.0.1:11434"),
        "qwen2.5:7b",
        SourceLanguage.Auto,
        0.75);
}

public interface ITextTranslating
{
    Task<string> Translate(string text, SourceLanguage sourceLanguage, string model, Uri endpoint);
}
