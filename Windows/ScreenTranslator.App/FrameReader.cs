namespace ScreenTranslator;

internal sealed class FrameRead
{
    public FrameRead(IReadOnlyList<RecognizedLine> lines, double pixelScale, ulong? hash, bool changed)
    {
        Lines = lines;
        PixelScale = pixelScale;
        Hash = hash;
        Changed = changed;
    }

    public IReadOnlyList<RecognizedLine> Lines { get; }
    public double PixelScale { get; }
    public ulong? Hash { get; }
    public bool Changed { get; }
}

internal static class FrameReader
{
    public static async Task<FrameRead> ReadAsync(
        CaptureRegion region,
        SourceLanguage language,
        ulong? previousHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (region.Width <= 1 || region.Height <= 1) throw new CaptureException("沒有截到畫面");
        var grabbed = RegionGrabber.Grab(region);
        var scale = grabbed.Width / region.Width;
        var hash = ImageHash.Digest(grabbed.Pixels, grabbed.Width, grabbed.Height);
        if (hash is not null && previousHash is not null && hash == previousHash)
            return new FrameRead([], scale, hash, false);
        var lines = await TextRecognizer.RecognizeAsync(grabbed.Pixels, grabbed.Width, grabbed.Height, language);
        cancellationToken.ThrowIfCancellationRequested();
        return new FrameRead(lines, scale, hash, true);
    }
}
