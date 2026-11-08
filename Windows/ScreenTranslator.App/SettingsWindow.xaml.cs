using System.Windows;
using System.Windows.Controls;

namespace ScreenTranslator;

public partial class SettingsWindow : Window
{
    private readonly AppModel _model;
    private bool _loading = true;

    public SettingsWindow(AppModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        LanguageBox.ItemsSource = new LanguageOption[]
        {
            new(SourceLanguage.Auto, "自動（日文優先）"),
            new(SourceLanguage.Japanese, "日文"),
            new(SourceLanguage.English, "英文"),
            new(SourceLanguage.SimplifiedChinese, "簡體中文"),
            new(SourceLanguage.TraditionalChinese, "繁體中文"),
        };
        LanguageBox.DisplayMemberPath = nameof(LanguageOption.Label);
        LanguageBox.SelectedItem = ((LanguageOption[])LanguageBox.ItemsSource)
            .First(option => option.Value == model.SourceLanguage);
        ModelBox.Text = model.Model;
        UrlBox.Text = model.OllamaUrl;
        OpacitySlider.Value = Math.Clamp(model.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        _loading = false;

        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (_loading || LanguageBox.SelectedItem is not LanguageOption option) return;
            _model.SourceLanguage = option.Value;
        };
        ModelBox.TextChanged += (_, _) =>
        {
            if (!_loading) _model.Model = ModelBox.Text;
        };
        UrlBox.TextChanged += (_, _) =>
        {
            if (!_loading) _model.OllamaUrl = UrlBox.Text;
        };
        OpacitySlider.ValueChanged += (_, _) =>
        {
            if (!_loading) _model.Opacity = OpacitySlider.Value;
        };
    }

    private void OnSelectRegion(object sender, RoutedEventArgs e) => _model.SelectRegion();

    private void OnToggle(object sender, RoutedEventArgs e) => _model.ToggleRunning();

    private sealed record LanguageOption(SourceLanguage Value, string Label);
}
