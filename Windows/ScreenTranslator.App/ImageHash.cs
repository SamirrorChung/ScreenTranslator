namespace ScreenTranslator;

internal static class ImageHash
{
    public static ulong? Digest(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;

        const int gridWidth = 96;
        const int gridHeight = 48;
        ulong digest = 14_695_981_039_346_656_037;
        for (var y = 0; y < gridHeight; y++)
        {
            var sourceY = (y + 0.5) * height / gridHeight - 0.5;
            for (var x = 0; x < gridWidth; x++)
            {
                var sourceX = (x + 0.5) * width / gridWidth - 0.5;
                digest ^= Sample(bgra, width, height, sourceX, sourceY);
                digest *= 1_099_511_628_211;
            }
        }
        return digest;
    }

    private static byte Sample(byte[] bgra, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var tx = x - x0;
        var ty = y - y0;
        var luma =
            Luma(bgra, width, x0, y0) * (1 - tx) * (1 - ty) +
            Luma(bgra, width, x1, y0) * tx * (1 - ty) +
            Luma(bgra, width, x0, y1) * (1 - tx) * ty +
            Luma(bgra, width, x1, y1) * tx * ty;
        return (byte)Math.Clamp((int)Math.Round(luma), 0, 255);
    }

    private static double Luma(byte[] bgra, int width, int x, int y)
    {
        var index = (y * width + x) * 4;
        var blue = bgra[index];
        var green = bgra[index + 1];
        var red = bgra[index + 2];
        return 0.299 * red + 0.587 * green + 0.114 * blue;
    }
}
