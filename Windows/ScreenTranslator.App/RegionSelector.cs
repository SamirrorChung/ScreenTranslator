using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScreenTranslator;

internal static class RegionSelector
{
    private static readonly List<SelectionWindow> Windows = new();
    private static Action<CaptureRegion>? _onSelect;
    private static Action? _onCancel;
    private static bool _tearingDown;

    public static void Begin(Action<CaptureRegion> onSelect, Action onCancel)
    {
        if (Windows.Count > 0) return;
        var monitors = Monitors.All();
        if (monitors.Count == 0)
        {
            onCancel();
            return;
        }

        _onSelect = onSelect;
        _onCancel = onCancel;
        _tearingDown = false;
        foreach (var monitor in monitors)
        {
            var window = new SelectionWindow(monitor);
            window.Committed += Finish;
            window.Cancelled += Cancel;
            window.Closed += (_, _) =>
            {
                if (!_tearingDown) Cancel();
            };
            Windows.Add(window);
        }

        foreach (var window in Windows) window.Show();
        Windows[0].Activate();
    }

    private static void Finish(CaptureRegion region)
    {
        var callback = _onSelect;
        Teardown();
        callback?.Invoke(region);
    }

    private static void Cancel()
    {
        if (_tearingDown) return;
        var callback = _onCancel;
        Teardown();
        callback?.Invoke();
    }

    private static void Teardown()
    {
        _tearingDown = true;
        _onSelect = null;
        _onCancel = null;
        foreach (var window in Windows.ToArray())
        {
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
            }
        }
        Windows.Clear();
    }
}

internal sealed class SelectionWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly SelectionCanvas _canvas;

    public event Action<CaptureRegion>? Committed;
    public event Action? Cancelled;

    public SelectionWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        var scale = Math.Max(monitor.Dpi, 96) / 96.0;
        Width = monitor.Width / scale;
        Height = monitor.Height / scale;
        _canvas = new SelectionCanvas();
        _canvas.Committed += rect =>
        {
            Committed?.Invoke(new CaptureRegion
            {
                DeviceName = _monitor.DeviceName,
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
            });
        };
        Content = _canvas;
        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => Place();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _monitor.Left,
            _monitor.Top,
            _monitor.Width,
            _monitor.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }
}

internal sealed class SelectionCanvas : Canvas
{
    private Point? _anchor;
    private Point? _current;

    public event Action<Rect>? Committed;

    public SelectionCanvas()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Cross;
        Focusable = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        _anchor = Clamp(e.GetPosition(this));
        _current = _anchor;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || _anchor is null) return;
        _current = Clamp(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        _current = Clamp(e.GetPosition(this));
        ReleaseMouseCapture();
        var band = Band();
        _anchor = null;
        _current = null;
        InvalidateVisual();
        if (band is { } rect && rect.Width >= 24 && rect.Height >= 24)
            Committed?.Invoke(rect);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var dim = new SolidColorBrush(Color.FromArgb(89, 0, 0, 0));
        dim.Freeze();
        var band = Band();
        if (band is { } hole && hole.Width > 1 && hole.Height > 1)
        {
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            group.Children.Add(new RectangleGeometry(bounds));
            group.Children.Add(new RectangleGeometry(hole));
            drawingContext.DrawGeometry(dim, null, group);
            drawingContext.DrawRectangle(null, new Pen(Brushes.White, 2), hole);
        }
        else
        {
            drawingContext.DrawRectangle(dim, null, bounds);
        }

        var text = new FormattedText(
            "拖曳圈選要翻譯的範圍　·　Esc 取消",
            CultureInfo.GetCultureInfo("zh-Hant"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
            16,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(Math.Max(0, (ActualWidth - text.Width) / 2), 28));
    }

    private Rect? Band()
    {
        if (_anchor is not { } anchor || _current is not { } current) return null;
        return new Rect(
            Math.Min(anchor.X, current.X),
            Math.Min(anchor.Y, current.Y),
            Math.Abs(current.X - anchor.X),
            Math.Abs(current.Y - anchor.Y));
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight)));
}
