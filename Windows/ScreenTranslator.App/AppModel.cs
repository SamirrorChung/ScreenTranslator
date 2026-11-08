using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace ScreenTranslator;

public sealed class AppModel : INotifyPropertyChanged
{
    private readonly TranslationPipeline _pipeline;
    private readonly Dispatcher _dispatcher;
    private readonly string _settingsPath;
    private SourceLanguage _sourceLanguage;
    private string _model;
    private string _ollamaUrl;
    private double _opacity;
    private bool _isRunning;
    private string _status = "尚未開始";
    private string _regionDescription = "尚未圈選";
    private string _ocrWarning = "";
    private CaptureRegion? _region;
    private OverlayWindow? _overlay;
    private CancellationTokenSource? _loop;
    private bool _forceRefresh;
    private double _lastPixelScale = 1;

    public AppModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenTranslator",
            "settings.json");
        var stored = Load();
        _sourceLanguage = SourceLanguages.Parse(stored?.SourceLanguage);
        _model = string.IsNullOrWhiteSpace(stored?.Model) ? TranslatorConfig.Default.Model : stored.Model;
        _ollamaUrl = string.IsNullOrWhiteSpace(stored?.OllamaUrl)
            ? TranslatorConfig.Default.BaseUrl.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)
            : stored.OllamaUrl;
        _opacity = stored?.Opacity ?? TranslatorConfig.Default.Opacity;
        _region = stored?.Region;
        if (_region is not null)
            _regionDescription = _region.Summary;
        _pipeline = new TranslationPipeline(CurrentConfig() ?? TranslatorConfig.Default);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SourceLanguage SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            if (_sourceLanguage == value) return;
            _sourceLanguage = value;
            Notify();
            PersistAndApply(refreshTranslation: true);
        }
    }

    public string Model
    {
        get => _model;
        set
        {
            if (_model == value) return;
            _model = value;
            Notify();
            PersistAndApply(refreshTranslation: true);
        }
    }

    public string OllamaUrl
    {
        get => _ollamaUrl;
        set
        {
            if (_ollamaUrl == value) return;
            _ollamaUrl = value;
            Notify();
            PersistAndApply(refreshTranslation: true);
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (_opacity.Equals(value)) return;
            _opacity = value;
            Notify();
            Notify(nameof(OpacityPercent));
            PersistAndApply(refreshTranslation: false);
        }
    }

    public string OpacityPercent => $"{(int)Math.Round(_opacity * 100, MidpointRounding.AwayFromZero)}%";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            Notify();
            Notify(nameof(ToggleLabel));
        }
    }

    public string ToggleLabel => _isRunning ? "暫停" : "開始";

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            Notify();
        }
    }

    public string RegionDescription
    {
        get => _regionDescription;
        private set
        {
            if (_regionDescription == value) return;
            _regionDescription = value;
            Notify();
        }
    }

    public string OcrWarning
    {
        get => _ocrWarning;
        private set
        {
            if (_ocrWarning == value) return;
            _ocrWarning = value;
            Notify();
        }
    }

    public void Bootstrap() => RefreshOcrWarning();

    public void ToggleRunning()
    {
        if (IsRunning) Pause();
        else Start();
    }

    public void SelectRegion()
    {
        var resume = IsRunning;
        if (IsRunning) Pause(updateStatus: false);
        Status = "圈選範圍中…";
        RegionSelector.Begin(
            region =>
            {
                _region = region;
                RegionDescription = region.Summary;
                Save();
                Status = "已圈選範圍";
                if (resume) Start();
            },
            () =>
            {
                Status = "已取消圈選";
                if (resume) Start();
            });
    }

    public void PersistAndApply(bool refreshTranslation)
    {
        Save();
        RefreshOcrWarning();
        var config = CurrentConfig();
        if (config is null)
        {
            Status = "請檢查模型名稱與 Ollama 位址";
            return;
        }
        if (refreshTranslation) _forceRefresh = true;
        _ = ApplyConfigAsync(config, refreshTranslation);
    }

    private async Task ApplyConfigAsync(TranslatorConfig config, bool refreshTranslation)
    {
        var command = await _pipeline.ReplaceConfig(config);
        if (!IsRunning || refreshTranslation) return;
        var note = command.Kind == OverlayCommandKind.Show ? PipelineNote.Cached : PipelineNote.None;
        Apply(new PipelineUpdate(command, note), _lastPixelScale);
    }

    private LoopSnapshot ConsumeSnapshot()
    {
        var refresh = _forceRefresh;
        _forceRefresh = false;
        return new LoopSnapshot(IsRunning, _region, _sourceLanguage, refresh, _pipeline);
    }

    private void Apply(PipelineUpdate update, double pixelScale)
    {
        if (!IsRunning) return;
        _lastPixelScale = pixelScale;
        switch (update.Command.Kind)
        {
            case OverlayCommandKind.Clear:
                _overlay?.SetMasks([], pixelScale);
                break;
            case OverlayCommandKind.Show:
                _overlay?.SetMasks(update.Command.Masks, pixelScale);
                break;
        }

        switch (update.Note.Kind)
        {
            case PipelineNoteKind.None:
                if (update.Command.Kind == OverlayCommandKind.Clear)
                    Status = "目前沒有文字";
                break;
            case PipelineNoteKind.Translated:
            case PipelineNoteKind.Cached:
                Status = "翻譯中";
                break;
            case PipelineNoteKind.Failed:
                var message = update.Note.Message ?? "翻譯失敗";
                Status = update.Command.Kind == OverlayCommandKind.Keep ? $"{message}，保留上一句" : message;
                break;
        }
    }

    private void NoteCaptureFailure(string message)
    {
        var text = "擷取失敗：" + message;
        if (Status != text) Status = text;
    }

    private void NoteCaptureHealthy()
    {
        if (Status.StartsWith("擷取失敗", StringComparison.Ordinal))
            Status = "翻譯中";
    }

    private void Start()
    {
        if (IsRunning) return;
        if (_region is null)
        {
            Status = "請先圈選區域";
            return;
        }
        if (Monitors.Find(_region.DeviceName) is null)
        {
            Status = "找不到當初圈選的螢幕";
            return;
        }
        if (CurrentConfig() is null)
        {
            Status = "請檢查模型名稱與 Ollama 位址";
            return;
        }
        RefreshOcrWarning();
        if (!TextRecognizer.IsAvailable(_sourceLanguage))
        {
            Status = TextRecognizer.MissingPackMessage(_sourceLanguage);
            return;
        }

        IsRunning = true;
        Status = "翻譯中";
        ShowOverlay(_region);
        _loop?.Cancel();
        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pipeline.Reset();
                await RunLoop(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                try
                {
                    await _dispatcher.InvokeAsync(() => NoteCaptureFailure(ex.Message));
                }
                catch (Exception)
                {
                }
            }
        }, token);
    }

    private void Pause(bool updateStatus = true)
    {
        IsRunning = false;
        _loop?.Cancel();
        _loop = null;
        if (_overlay is not null)
        {
            _overlay.Close();
            _overlay = null;
        }
        _ = _pipeline.Reset();
        if (updateStatus) Status = "已暫停";
    }

    private void ShowOverlay(CaptureRegion region)
    {
        var monitor = Monitors.Find(region.DeviceName);
        if (monitor is null) return;
        var panel = new OverlayWindow(region, monitor);
        panel.Show();
        _overlay = panel;
    }

    private async Task RunLoop(CancellationToken token)
    {
        ulong? lastHash = null;
        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            LoopSnapshot snapshot;
            try
            {
                snapshot = await _dispatcher.InvokeAsync(ConsumeSnapshot);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!snapshot.IsRunning || snapshot.Region is null) return;

            try
            {
                var frame = await FrameReader.ReadAsync(
                    snapshot.Region,
                    snapshot.Language,
                    snapshot.ForceRefresh ? null : lastHash,
                    token);
                if (frame.Hash is { } hash) lastHash = hash;
                if (frame.Changed)
                {
                    var update = await snapshot.Pipeline.Update(frame.Lines, DateTime.UtcNow);
                    await _dispatcher.InvokeAsync(() => Apply(update, frame.PixelScale));
                }
                else
                {
                    await _dispatcher.InvokeAsync(NoteCaptureHealthy);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                try
                {
                    await _dispatcher.InvokeAsync(() => NoteCaptureFailure(message));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var interval = TimeSpan.FromMilliseconds(250);
            if (elapsed < interval)
            {
                try
                {
                    await Task.Delay(interval - elapsed, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void RefreshOcrWarning()
    {
        OcrWarning = TextRecognizer.IsAvailable(_sourceLanguage)
            ? ""
            : TextRecognizer.MissingPackMessage(_sourceLanguage);
    }

    private TranslatorConfig? CurrentConfig() => MakeConfig(_sourceLanguage, _model, _ollamaUrl, _opacity);

    private static TranslatorConfig? MakeConfig(SourceLanguage language, string model, string ollamaUrl, double opacity)
    {
        var trimmedUrl = ollamaUrl.Trim();
        var trimmedModel = model.Trim();
        if (trimmedModel.Length == 0) return null;
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var url)) return null;
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(url.Host)) return null;
        return new TranslatorConfig(url, trimmedModel, language, opacity);
    }

    private StoredSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;
            return JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_settingsPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var stored = new StoredSettings
            {
                SourceLanguage = _sourceLanguage.RawValue(),
                Model = _model,
                OllamaUrl = _ollamaUrl,
                Opacity = _opacity,
                Region = _region,
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class LoopSnapshot
    {
        public LoopSnapshot(bool isRunning, CaptureRegion? region, SourceLanguage language, bool forceRefresh, TranslationPipeline pipeline)
        {
            IsRunning = isRunning;
            Region = region;
            Language = language;
            ForceRefresh = forceRefresh;
            Pipeline = pipeline;
        }

        public bool IsRunning { get; }
        public CaptureRegion? Region { get; }
        public SourceLanguage Language { get; }
        public bool ForceRefresh { get; }
        public TranslationPipeline Pipeline { get; }
    }

    private sealed class StoredSettings
    {
        [JsonPropertyName("sourceLanguage")]
        public string? SourceLanguage { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("ollamaURL")]
        public string? OllamaUrl { get; set; }

        [JsonPropertyName("opacity")]
        public double? Opacity { get; set; }

        [JsonPropertyName("region")]
        public CaptureRegion? Region { get; set; }
    }
}
