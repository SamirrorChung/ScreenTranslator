using System.Windows;
using System.Windows.Media;

namespace ScreenTranslator;

public partial class LogWindow : Window
{
    private static readonly Brush NormalBrush = Frozen(Color.FromRgb(0x66, 0x66, 0x66));
    private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xE0, 0x7A, 0x00));
    private static LogWindow? _window;

    public LogWindow()
    {
        InitializeComponent();
        TranslationLog.Changed += OnChanged;
        Closed += (_, _) =>
        {
            TranslationLog.Changed -= OnChanged;
            if (ReferenceEquals(_window, this)) _window = null;
        };
        Render(TranslationLog.Latest);
    }

    public static void ShowLatest()
    {
        if (_window is null)
            _window = new LogWindow();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();
    }

    public static void CloseLatest() => _window?.Close();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnChanged()
    {
        var latest = TranslationLog.Latest;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded) return;
            Render(latest);
        });
    }

    private void Render(TranslationExchange? exchange)
    {
        if (exchange is null)
        {
            Header.Text = "還沒有送出過翻譯。";
            Header.Foreground = NormalBrush;
            PromptBox.Text = "";
            ContentBox.Text = "";
            ResponseBox.Text = "";
            return;
        }

        var when = exchange.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Header.Text = string.IsNullOrEmpty(exchange.Error)
            ? $"{when}  ·  {exchange.Model}"
            : $"{when}  ·  {exchange.Model}  ·  {exchange.Error}";
        Header.Foreground = string.IsNullOrEmpty(exchange.Error) ? NormalBrush : ErrorBrush;
        PromptBox.Text = exchange.SystemPrompt;
        ContentBox.Text = exchange.Content;
        ResponseBox.Text = exchange.Response ?? "";
    }
}
