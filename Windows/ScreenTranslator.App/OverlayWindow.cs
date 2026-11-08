using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenTranslator;

internal sealed class OverlayWindow : Window
{
    private readonly OverlayCanvas _canvas;
    private readonly CaptureRegion _region;
    private readonly MonitorInfo _monitor;

    public OverlayWindow(CaptureRegion region, MonitorInfo monitor)
    {
        _region = region;
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        IsHitTestVisible = false;
        Width = Math.Max(region.Width, 1);
        Height = Math.Max(region.Height, 1);
        _canvas = new OverlayCanvas();
        Content = _canvas;
        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => Place();
    }

    public void SetMasks(IReadOnlyList<TextMask> masks, double pixelScale)
    {
        _canvas.PixelScale = pixelScale;
        _canvas.Masks = masks;
        _canvas.InvalidateVisual();
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.MakeClickThrough(hwnd);
        NativeMethods.ExcludeFromCapture(hwnd);
        var physical = _monitor.ToPhysical(_region.X, _region.Y, _region.Width, _region.Height);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            physical.X,
            physical.Y,
            physical.Width,
            physical.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}

internal sealed class OverlayCanvas : FrameworkElement
{
    private static readonly (double X, double Y)[] ShadowOffsets =
    [
        (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1),
    ];

    public double PixelScale { get; set; } = 1;
    public IReadOnlyList<TextMask> Masks { get; set; } = Array.Empty<TextMask>();

    public OverlayCanvas()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (PixelScale <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var mask in Masks)
        {
            if (string.IsNullOrEmpty(mask.Text)) continue;
            Draw(drawingContext, mask, dpi);
        }
    }

    private void Draw(DrawingContext drawingContext, TextMask mask, double pixelsPerDip)
    {
        var rect = new Rect(
            mask.X / PixelScale,
            mask.Y / PixelScale,
            Math.Max(mask.Width / PixelScale, 8),
            Math.Max(mask.Height / PixelScale, 8));
        rect.Inflate(6, 4);
        if (rect.Height < 22) rect.Height = 22;

        var fontSize = Math.Min(28, Math.Max(13, rect.Height * 0.55));
        var measured = Measure(mask.Text, fontSize, pixelsPerDip);
        while (measured.Width > Math.Max(rect.Width - 12, 8) && fontSize > 11)
        {
            fontSize -= 1;
            measured = Measure(mask.Text, fontSize, pixelsPerDip);
        }

        var neededWidth = measured.Width + 16;
        if (neededWidth > rect.Width)
        {
            var growth = neededWidth - rect.Width;
            rect.X -= growth / 2;
            rect.Width = neededWidth;
        }

        rect = Clamp(rect, new Size(ActualWidth, ActualHeight));
        if (rect.Width <= 4 || rect.Height <= 4) return;

        var alpha = (byte)Math.Clamp((int)Math.Round(mask.Opacity * 255), 0, 255);
        var fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        fill.Freeze();
        drawingContext.DrawRoundedRectangle(fill, null, rect, 5, 5);

        var contentWidth = Math.Max(rect.Width - 12, 1);
        var originY = rect.Y + (rect.Height - measured.Height) / 2;
        var shadow = Format(mask.Text, fontSize, Color.FromArgb(217, 0, 0, 0), contentWidth, pixelsPerDip);
        var text = Format(mask.Text, fontSize, Colors.White, contentWidth, pixelsPerDip);
        foreach (var (dx, dy) in ShadowOffsets)
            drawingContext.DrawText(shadow, new Point(rect.X + 6 + dx, originY + dy));
        drawingContext.DrawText(text, new Point(rect.X + 6, originY));
    }

    private static Size Measure(string text, double fontSize, double pixelsPerDip)
    {
        var formatted = Format(text, fontSize, Colors.White, double.PositiveInfinity, pixelsPerDip);
        return new Size(formatted.Width, formatted.Height);
    }

    private static FormattedText Format(string text, double fontSize, Color color, double maxWidth, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-Hant"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize,
            new SolidColorBrush(color),
            pixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        if (!double.IsPositiveInfinity(maxWidth))
            formatted.MaxTextWidth = maxWidth;
        return formatted;
    }

    private static Rect Clamp(Rect rect, Size bounds)
    {
        const double inset = 2;
        var maxWidth = Math.Max(bounds.Width - inset * 2, 1);
        var maxHeight = Math.Max(bounds.Height - inset * 2, 1);
        if (rect.Width > maxWidth)
        {
            rect.Width = maxWidth;
            rect.X = inset;
        }
        else if (rect.X < inset)
        {
            rect.X = inset;
        }
        else if (rect.Right > bounds.Width - inset)
        {
            rect.X = bounds.Width - inset - rect.Width;
        }

        if (rect.Height > maxHeight)
        {
            rect.Height = maxHeight;
            rect.Y = inset;
        }
        else if (rect.Y < inset)
        {
            rect.Y = inset;
        }
        else if (rect.Bottom > bounds.Height - inset)
        {
            rect.Y = bounds.Height - inset - rect.Height;
        }
        return rect;
    }
}
