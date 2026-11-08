namespace ScreenTranslator;

internal static class TextRecognizer
{
    public static string MissingPackMessage(SourceLanguage language) => OneOcr.MissingMessage;

    public static bool IsAvailable(SourceLanguage language)
    {
        try
        {
            return OneOcr.IsInstalled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(
        byte[] bgra,
        int width,
        int height,
        SourceLanguage language)
    {
        _ = language;
        return Task.Run(() => OneOcr.Recognize(bgra, width, height));
    }
}
