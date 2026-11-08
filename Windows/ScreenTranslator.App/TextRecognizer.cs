using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenTranslator;

internal static class TextRecognizer
{
    private static readonly Dictionary<string, OcrEngine> Engines = new();

    public static string MissingPackMessage(SourceLanguage language) =>
        $"請到「設定 › 時間與語言 › 語言與地區」安裝{LanguageName(language)}的光學字元辨識";

    public static bool IsAvailable(SourceLanguage language)
    {
        try
        {
            return FindLanguage(OcrTag(language)) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(byte[] bgra, int width, int height, SourceLanguage language)
    {
        var tag = OcrTag(language);
        var winrtLanguage = FindLanguage(tag) ?? throw new CaptureException(MissingPackMessage(language));
        if (!Engines.TryGetValue(winrtLanguage.LanguageTag, out var engine))
        {
            engine = OcrEngine.TryCreateFromLanguage(winrtLanguage)
                ?? throw new CaptureException(MissingPackMessage(language));
            Engines[winrtLanguage.LanguageTag] = engine;
        }

        using var bitmap = CreateBitmap(bgra, width, height);
        var result = await engine.RecognizeAsync(bitmap);
        var lines = new List<RecognizedLine>();
        foreach (var line in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text) || line.Words.Count == 0) continue;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (var word in line.Words)
            {
                var box = word.BoundingRect;
                minX = Math.Min(minX, box.X);
                minY = Math.Min(minY, box.Y);
                maxX = Math.Max(maxX, box.X + box.Width);
                maxY = Math.Max(maxY, box.Y + box.Height);
            }
            lines.Add(new RecognizedLine(line.Text, minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY), 1));
        }
        return lines;
    }

    private static string OcrTag(SourceLanguage language) => language switch
    {
        SourceLanguage.English => "en",
        SourceLanguage.SimplifiedChinese => "zh-Hans",
        SourceLanguage.TraditionalChinese => "zh-Hant",
        _ => "ja",
    };

    private static string LanguageName(SourceLanguage language) => language switch
    {
        SourceLanguage.English => "英文",
        SourceLanguage.SimplifiedChinese => "簡體中文",
        SourceLanguage.TraditionalChinese => "繁體中文",
        _ => "日文",
    };

    private static Windows.Globalization.Language? FindLanguage(string tag)
    {
        Windows.Globalization.Language? prefix = null;
        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            var actual = language.LanguageTag;
            if (actual.Equals(tag, StringComparison.OrdinalIgnoreCase)) return language;
            if (prefix is null && actual.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase))
                prefix = language;
        }
        return prefix;
    }

    private static SoftwareBitmap CreateBitmap(byte[] bgra, int width, int height)
    {
        var writer = new DataWriter();
        try
        {
            writer.WriteBytes(bgra);
            return SoftwareBitmap.CreateCopyFromBuffer(
                writer.DetachBuffer(),
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Ignore);
        }
        finally
        {
            writer.Dispose();
        }
    }
}
