using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenTranslator;

internal sealed class GrabbedFrame
{
    public GrabbedFrame(byte[] pixels, int width, int height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
}

internal static class RegionGrabber
{
    public static GrabbedFrame Grab(CaptureRegion region)
    {
        var monitor = Monitors.Find(region.DeviceName) ?? throw new CaptureException("找不到圈選的螢幕");
        var (x, y, width, height) = monitor.ToPhysical(region.X, region.Y, region.Width, region.Height);
        if (width <= 1 || height <= 1) throw new CaptureException("沒有截到畫面");

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new CaptureException("沒有截到畫面");
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) throw new CaptureException("沒有截到畫面");
            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, NativeMethods.SRCCOPY))
                throw new CaptureException("沒有截到畫面");
            NativeMethods.SelectObject(memoryDc, previous);
            previous = IntPtr.Zero;

            using var source = Image.FromHbitmap(bitmap);
            using var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy))
                graphics.DrawImage(source, 0, 0, width, height);

            var pixels = new byte[width * height * 4];
            var locked = copy.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rowBytes = width * 4;
                for (var row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(locked.Scan0, row * locked.Stride), pixels, row * rowBytes, rowBytes);
            }
            finally
            {
                copy.UnlockBits(locked);
            }
            return new GrabbedFrame(pixels, width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
