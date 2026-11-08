namespace ScreenTranslator;

public sealed class TranslationPipeline
{
    private readonly object _gate = new();
    private readonly ITextTranslating _translator;
    private readonly TranslationCache _cache;
    private TranslatorConfig _config;
    private string? _lastKey;
    private string? _lastTranslation;
    private List<TextMask> _lastMasks = new();
    private DateTime? _emptySince;
    private int _generation;

    public TranslationPipeline(TranslatorConfig config, int cacheLimit = 100)
        : this(config, new OllamaClient(), cacheLimit)
    {
    }

    public TranslationPipeline(TranslatorConfig config, ITextTranslating translator, int cacheLimit = 100)
    {
        _config = config;
        _translator = translator;
        _cache = new TranslationCache(cacheLimit);
    }

    public Task Reset()
    {
        lock (_gate)
        {
            _generation++;
            _lastKey = null;
            _lastTranslation = null;
            _lastMasks = new List<TextMask>();
            _emptySince = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>Updates opacity on masks already on screen. Cached sentences stay cached.</summary>
    public Task<OverlayCommand> ReplaceConfig(TranslatorConfig newConfig)
    {
        lock (_gate)
        {
            _config = newConfig;
            if (_lastMasks.Count == 0) return Task.FromResult(OverlayCommand.Clear);
            var updated = new List<TextMask>(_lastMasks.Count);
            foreach (var mask in _lastMasks)
                updated.Add(mask with { Opacity = _config.Opacity });
            _lastMasks = updated;
            return Task.FromResult(OverlayCommand.Show(updated));
        }
    }

    public async Task<PipelineUpdate> Update(IReadOnlyList<RecognizedLine> lines, DateTime now)
    {
        Pending? pending;
        lock (_gate)
        {
            var accepted = UsableLines(lines);
            if (accepted.Count == 0) return ClearOrHold(now);

            _emptySince = null;
            _generation++;
            var token = _generation;
            var ordered = accepted
                .OrderBy(line => line.Y)
                .ThenBy(line => line.X)
                .ToList();
            var signature = string.Join("\n", ordered.Select(line => TranslationText.NormalizeLine(line.Text)));
            var key = CacheKey(signature);

            if (key == _lastKey && _lastTranslation is not null)
            {
                var same = MakeMasks(ordered, _lastTranslation);
                _lastMasks = same;
                return new PipelineUpdate(same.Count == 0 ? OverlayCommand.Clear : OverlayCommand.Show(same), PipelineNote.Cached);
            }

            var cached = _cache.Value(key);
            if (cached is not null)
            {
                _lastKey = key;
                _lastTranslation = cached;
                var masks = MakeMasks(ordered, cached);
                _lastMasks = masks;
                return new PipelineUpdate(masks.Count == 0 ? OverlayCommand.Clear : OverlayCommand.Show(masks), PipelineNote.Cached);
            }

            pending = new Pending(token, key, signature, ordered, _config.SourceLanguage, _config.Model, _config.BaseUrl);
        }

        string translated;
        try
        {
            translated = await _translator.Translate(pending.Signature, pending.Language, pending.Model, pending.Endpoint);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (pending.Token != _generation)
                    return new PipelineUpdate(OverlayCommand.Keep, PipelineNote.None);
                var message = ex is TranslationException translation ? translation.Message : "翻譯失敗";
                return Fail(message);
            }
        }

        lock (_gate)
        {
            if (pending.Token != _generation)
                return new PipelineUpdate(OverlayCommand.Keep, PipelineNote.None);
            var cleaned = TranslationText.Clean(translated);
            if (cleaned.Length == 0) return Fail("Ollama 沒有回傳譯文");
            _cache.Insert(cleaned, pending.Key);
            _lastKey = pending.Key;
            _lastTranslation = cleaned;
            var masks = MakeMasks(pending.Lines, cleaned);
            _lastMasks = masks;
            if (masks.Count == 0)
                return new PipelineUpdate(OverlayCommand.Clear, PipelineNote.None);
            return new PipelineUpdate(OverlayCommand.Show(masks), PipelineNote.Translated);
        }
    }

    private List<RecognizedLine> UsableLines(IReadOnlyList<RecognizedLine> lines)
    {
        var accepted = new List<RecognizedLine>();
        foreach (var line in lines)
        {
            if (line.Confidence >= _config.MinimumConfidence &&
                TranslationText.NormalizeLine(line.Text).Length > 0)
            {
                accepted.Add(line);
            }
        }
        return accepted;
    }

    private string CacheKey(string signature) =>
        $"{_config.BaseUrl.AbsoluteUri}|{_config.Model}|{_config.SourceLanguage.RawValue()}|{signature}";

    private PipelineUpdate ClearOrHold(DateTime now)
    {
        if (_lastMasks.Count == 0)
            return new PipelineUpdate(OverlayCommand.Clear, PipelineNote.None);
        _emptySince ??= now;
        if (now - _emptySince.Value >= _config.EmptyHold)
        {
            _lastMasks = new List<TextMask>();
            _lastKey = null;
            _lastTranslation = null;
            _emptySince = null;
            _generation++;
            return new PipelineUpdate(OverlayCommand.Clear, PipelineNote.None);
        }
        return new PipelineUpdate(OverlayCommand.Keep, PipelineNote.WaitingToClear);
    }

    private PipelineUpdate Fail(string message)
    {
        if (_lastMasks.Count == 0)
            return new PipelineUpdate(OverlayCommand.Clear, PipelineNote.Failed(message));
        return new PipelineUpdate(OverlayCommand.Keep, PipelineNote.Failed(message));
    }

    private List<TextMask> MakeMasks(IReadOnlyList<RecognizedLine> lines, string translation)
    {
        var parts = translation.Split('\n').ToList();
        if (parts.Count > 0 && parts[^1].Trim().Length == 0)
            parts.RemoveAt(parts.Count - 1);
        if (parts.Count == lines.Count)
        {
            var masks = new List<TextMask>();
            for (var i = 0; i < lines.Count; i++)
            {
                var text = parts[i].Trim();
                if (text.Length == 0) continue;
                var line = lines[i];
                masks.Add(new TextMask(line.X, line.Y, line.Width, line.Height, text, _config.Opacity));
            }
            return masks;
        }

        var combined = translation.Trim();
        var box = Union(lines);
        if (combined.Length == 0 || box is null) return new List<TextMask>();
        return
        [
            new TextMask(box.Value.X, box.Value.Y, box.Value.Width, box.Value.Height, combined, _config.Opacity),
        ];
    }

    private static (double X, double Y, double Width, double Height)? Union(IReadOnlyList<RecognizedLine> lines)
    {
        if (lines.Count == 0) return null;
        var first = lines[0];
        var minX = first.X;
        var minY = first.Y;
        var maxX = first.X + first.Width;
        var maxY = first.Y + first.Height;
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            minX = Math.Min(minX, line.X);
            minY = Math.Min(minY, line.Y);
            maxX = Math.Max(maxX, line.X + line.Width);
            maxY = Math.Max(maxY, line.Y + line.Height);
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private sealed record Pending(
        int Token,
        string Key,
        string Signature,
        List<RecognizedLine> Lines,
        SourceLanguage Language,
        string Model,
        Uri Endpoint);
}
