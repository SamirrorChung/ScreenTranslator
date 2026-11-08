namespace ScreenTranslator.Tests;

public class PipelineTests
{
    [Fact]
    public async Task EmptyFrameStaysTransparent()
    {
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, new ScriptedTranslator());
        var update = await pipeline.Update([], At(0));
        Assert.Equal(OverlayCommand.Clear, update.Command);
        Assert.Equal(PipelineNote.None, update.Note);
    }

    [Fact]
    public async Task LowConfidenceIsTreatedAsNoText()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("不該出現"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var update = await pipeline.Update([Line("こんにちは", confidence: 0.05)], At(0));
        Assert.Equal(OverlayCommand.Clear, update.Command);
        Assert.Equal(0, translator.Calls);
    }

    [Fact]
    public async Task TranslatesThenReusesCacheWhenTheLineMoves()
    {
        var translator = new ScriptedTranslator(
            ScriptedTranslator.Result.Success("你好"),
            ScriptedTranslator.Result.Success("不該用到"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var now = At(10);
        var first = await pipeline.Update([Line("こんにちは", x: 4)], now);
        var second = await pipeline.Update([Line("こんにちは", x: 40)], now);

        var firstMasks = Shown(first);
        var secondMasks = Shown(second);
        Assert.Equal(PipelineNote.Translated, first.Note);
        Assert.Equal(PipelineNote.Cached, second.Note);
        Assert.Equal("你好", firstMasks[0].Text);
        Assert.Equal(4, firstMasks[0].X);
        Assert.Equal(40, secondMasks[0].X);
        Assert.Equal("你好", secondMasks[0].Text);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task WhitespaceDifferencesDoNotRetranslate()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("你好"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var now = At(3);
        await pipeline.Update([Line("こんにちは")], now);
        var again = await pipeline.Update([Line("  こんにちは　 ")], now);
        Assert.Equal(PipelineNote.Cached, again.Note);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task EmptyTextHoldsThenClears()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("你好"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var start = At(20);
        await pipeline.Update([Line("こんにちは")], start);

        var immediate = await pipeline.Update([], start);
        var stillHolding = await pipeline.Update([], start.AddSeconds(0.39));
        var cleared = await pipeline.Update([], start.AddSeconds(0.41));

        Assert.Equal(OverlayCommand.Keep, immediate.Command);
        Assert.Equal(PipelineNote.WaitingToClear, immediate.Note);
        Assert.Equal(OverlayCommand.Keep, stillHolding.Command);
        Assert.Equal(OverlayCommand.Clear, cleared.Command);
    }

    [Fact]
    public async Task TextReturningDuringHoldDoesNotClear()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("你好"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var start = At(30);
        await pipeline.Update([Line("こんにちは")], start);
        await pipeline.Update([], start);
        var back = await pipeline.Update([Line("こんにちは")], start.AddSeconds(0.2));
        Assert.Equal(PipelineNote.Cached, back.Note);
        Assert.Equal("你好", Shown(back)[0].Text);
    }

    [Fact]
    public async Task FailedTranslationKeepsPreviousMask()
    {
        var translator = new ScriptedTranslator(
            ScriptedTranslator.Result.Success("你好"),
            ScriptedTranslator.Result.Failure(TranslationException.Unreachable()));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var now = At(40);
        await pipeline.Update([Line("こんにちは")], now);
        var failed = await pipeline.Update([Line("さようなら")], now);
        Assert.Equal(OverlayCommand.Keep, failed.Command);
        Assert.Equal(PipelineNote.Failed("無法連上 Ollama"), failed.Note);
    }

    [Fact]
    public async Task FailedFirstTranslationStaysTransparent()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Failure(TranslationException.Unreachable()));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var failed = await pipeline.Update([Line("こんにちは")], At(50));
        Assert.Equal(OverlayCommand.Clear, failed.Command);
        Assert.Equal(PipelineNote.Failed("無法連上 Ollama"), failed.Note);
    }

    [Fact]
    public async Task MismatchedLineCountUsesOneMask()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("合併的譯文"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var update = await pipeline.Update(
            [
                Line("あ", x: 10, y: 5, width: 20, height: 10),
                Line("い", x: 40, y: 5, width: 20, height: 10),
            ],
            At(60));
        var masks = Shown(update);
        Assert.Single(masks);
        Assert.Equal("合併的譯文", masks[0].Text);
        Assert.Equal(10, masks[0].X);
        Assert.Equal(50, masks[0].Width);
        Assert.Equal("あ\nい", translator.LastInput);
    }

    [Fact]
    public async Task MatchingLinesKeepSeparateMasks()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("甲\n乙\n"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        var update = await pipeline.Update([Line("a", y: 0), Line("b", y: 30)], At(70));
        Assert.Equal(["甲", "乙"], Shown(update).Select(mask => mask.Text).ToArray());
    }

    [Fact]
    public async Task OpacityChangeDoesNotRetranslate()
    {
        var translator = new ScriptedTranslator(ScriptedTranslator.Result.Success("你好"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator);
        await pipeline.Update([Line("こんにちは")], At(80));
        var faded = new TranslatorConfig(
            TranslatorConfig.Default.BaseUrl,
            TranslatorConfig.Default.Model,
            TranslatorConfig.Default.SourceLanguage,
            0.4);
        var command = await pipeline.ReplaceConfig(faded);
        Assert.Equal(OverlayCommandKind.Show, command.Kind);
        Assert.Equal(0.4, command.Masks[0].Opacity);
        Assert.Equal("你好", command.Masks[0].Text);
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task CacheDropsTheOldestSentence()
    {
        var translator = new ScriptedTranslator(
            ScriptedTranslator.Result.Success("一"),
            ScriptedTranslator.Result.Success("二"),
            ScriptedTranslator.Result.Success("一再譯"));
        var pipeline = new TranslationPipeline(TranslatorConfig.Default, translator, cacheLimit: 1);
        var now = At(90);
        await pipeline.Update([Line("a")], now);
        await pipeline.Update([Line("b")], now);
        var again = await pipeline.Update([Line("a")], now);
        Assert.Equal(PipelineNote.Translated, again.Note);
        Assert.Equal("一再譯", Shown(again)[0].Text);
        Assert.Equal(3, translator.Calls);
    }

    [Fact]
    public void PromptAsksForTraditionalChinese()
    {
        var prompt = TranslationText.SystemPrompt(SourceLanguage.Japanese);
        Assert.Contains("繁體中文", prompt);
        Assert.Contains("日文", prompt);
    }

    [Fact]
    public void CleanDropsFencesAndLabels()
    {
        Assert.Equal("你好", TranslationText.Clean("```\n你好\n```"));
        Assert.Equal("你好", TranslationText.Clean("「你好」"));
        Assert.Equal("你好", TranslationText.Clean("譯文：你好"));
    }

    private static IReadOnlyList<TextMask> Shown(PipelineUpdate update)
    {
        Assert.Equal(OverlayCommandKind.Show, update.Command.Kind);
        return update.Command.Masks;
    }

    private static DateTime At(double seconds) => DateTime.UnixEpoch.AddSeconds(seconds);

    private static RecognizedLine Line(
        string text,
        double x = 0,
        double y = 0,
        double width = 80,
        double height = 20,
        double confidence = 0.9) =>
        new(text, x, y, width, height, confidence);
}

internal sealed class ScriptedTranslator : ITextTranslating
{
    private readonly Queue<Result> _results;

    public ScriptedTranslator(params Result[] results)
    {
        _results = new Queue<Result>(results);
    }

    public int Calls { get; private set; }
    public string LastInput { get; private set; } = "";

    public Task<string> Translate(string text, SourceLanguage sourceLanguage, string model, Uri endpoint)
    {
        Calls++;
        LastInput = text;
        if (_results.Count == 0) throw TranslationException.EmptyResponse();
        return Task.FromResult(_results.Dequeue().Get());
    }

    public abstract record Result
    {
        public abstract string Get();
        public static Result Success(string text) => new SuccessResult(text);
        public static Result Failure(TranslationException error) => new FailureResult(error);

        private sealed record SuccessResult(string Text) : Result
        {
            public override string Get() => Text;
        }

        private sealed record FailureResult(TranslationException Error) : Result
        {
            public override string Get() => throw Error;
        }
    }
}
