using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreenTranslator;

internal static class OneOcr
{
    public const string MissingMessage = "找不到剪取工具的文字辨識。請確認 Microsoft Store 的「剪取工具」已安裝。";

    private const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";
    private static readonly string[] FileNames = ["oneocr.dll", "oneocr.onemodel", "onnxruntime.dll"];
    private static readonly object Gate = new();
    private static bool _ready;
    private static long _pipeline;
    private static long _options;
    private static RunPipelineDelegate? _run;
    private static GetCountDelegate? _lineCount;
    private static GetAtDelegate? _getLine;
    private static GetTextDelegate? _lineContent;
    private static GetBoxDelegate? _lineBox;
    private static GetCountDelegate? _wordCount;
    private static GetAtDelegate? _getWord;
    private static GetConfidenceDelegate? _wordConfidence;
    private static ReleaseDelegate? _release;

    public static bool IsInstalled() => FindSourceDirectory() is not null;

    public static IReadOnlyList<RecognizedLine> Recognize(byte[] bgra, int width, int height)
    {
        if (width <= 1 || height <= 1 || bgra.Length < width * height * 4)
            throw new CaptureException("沒有截到畫面");

        lock (Gate)
        {
            EnsureReady();
            var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                var image = new Img
                {
                    Type = 3,
                    Width = width,
                    Height = height,
                    Step = width * 4L,
                    Data = handle.AddrOfPinnedObject(),
                };
                var code = _run!( _pipeline, ref image, _options, out var result);
                if (code != 0)
                    throw new CaptureException($"剪取工具文字辨識失敗（{code}）。");
                try
                {
                    return ReadLines(result, width, height);
                }
                finally
                {
                    _release!(result);
                }
            }
            finally
            {
                handle.Free();
            }
        }
    }

    private static void EnsureReady()
    {
        if (_ready) return;
        var source = FindSourceDirectory() ?? throw new CaptureException(MissingMessage);
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenTranslator",
            "oneocr");
        Directory.CreateDirectory(cache);
        foreach (var name in FileNames)
        {
            var from = Path.Combine(source, name);
            var to = Path.Combine(cache, name);
            var sourceInfo = new FileInfo(from);
            var destInfo = new FileInfo(to);
            if (destInfo.Exists && destInfo.Length == sourceInfo.Length) continue;
            try
            {
                File.Copy(from, to, true);
            }
            catch (IOException) when (File.Exists(to))
            {
            }
        }

        var library = NativeLibrary.Load(Path.Combine(cache, "oneocr.dll"));
        var createInit = Export<CreateInitDelegate>(library, "CreateOcrInitOptions");
        var setDelay = Export<SetDelayDelegate>(library, "OcrInitOptionsSetUseModelDelayLoad");
        var createPipeline = Export<CreatePipelineDelegate>(library, "CreateOcrPipeline");
        var createOptions = Export<CreateOptionsDelegate>(library, "CreateOcrProcessOptions");
        var setLines = Export<SetLinesDelegate>(library, "OcrProcessOptionsSetMaxRecognitionLineCount");
        _run = Export<RunPipelineDelegate>(library, "RunOcrPipeline");
        _lineCount = Export<GetCountDelegate>(library, "GetOcrLineCount");
        _getLine = Export<GetAtDelegate>(library, "GetOcrLine");
        _lineContent = Export<GetTextDelegate>(library, "GetOcrLineContent");
        _lineBox = Export<GetBoxDelegate>(library, "GetOcrLineBoundingBox");
        _wordCount = Export<GetCountDelegate>(library, "GetOcrLineWordCount");
        _getWord = Export<GetAtDelegate>(library, "GetOcrWord");
        _wordConfidence = Export<GetConfidenceDelegate>(library, "GetOcrWordConfidence");
        _release = Export<ReleaseDelegate>(library, "ReleaseOcrResult");

        Require(createInit(out var ctx), "啟動");
        Require(setDelay(ctx, 0), "啟動");
        Require(createPipeline(Path.Combine(cache, "oneocr.onemodel"), ModelKey, ctx, out _pipeline), "啟動");
        Require(createOptions(out _options), "啟動");
        Require(setLines(_options, 1000), "啟動");
        _ready = true;
    }

    private static IReadOnlyList<RecognizedLine> ReadLines(long result, int width, int height)
    {
        Require(_lineCount!(result, out var count), "讀取");
        var lines = new List<RecognizedLine>();
        for (long i = 0; i < count; i++)
        {
            if (_getLine!(result, i, out var line) != 0 || line == 0) continue;
            if (_lineContent!(line, out var textPtr) != 0) continue;
            var text = Marshal.PtrToStringUTF8(textPtr);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (_lineBox!(line, out var boxPtr) != 0 || boxPtr == IntPtr.Zero) continue;
            var box = Marshal.PtrToStructure<BoundingBox>(boxPtr);
            var minX = Math.Clamp(Min(box.X1, box.X2, box.X3, box.X4), 0, width);
            var minY = Math.Clamp(Min(box.Y1, box.Y2, box.Y3, box.Y4), 0, height);
            var maxX = Math.Clamp(Max(box.X1, box.X2, box.X3, box.X4), 0, width);
            var maxY = Math.Clamp(Max(box.Y1, box.Y2, box.Y3, box.Y4), 0, height);
            if (maxX - minX < 1 || maxY - minY < 1) continue;
            lines.Add(new RecognizedLine(text, minX, minY, maxX - minX, maxY - minY, Confidence(line)));
        }
        return lines;
    }

    private static double Confidence(long line)
    {
        if (_wordCount!(line, out var count) != 0 || count <= 0) return 1;
        double sum = 0;
        var counted = 0;
        for (long i = 0; i < count; i++)
        {
            if (_getWord!(line, i, out var word) != 0 || word == 0) continue;
            if (_wordConfidence!(word, out var confidence) != 0) continue;
            if (confidence <= 0 || confidence > 1) continue;
            sum += confidence;
            counted++;
        }
        return counted == 0 ? 1 : sum / counted;
    }

    private static string? FindSourceDirectory()
    {
        using var packages = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null) return null;
        string? found = null;
        foreach (var name in packages.GetSubKeyNames())
        {
            if (!name.StartsWith("Microsoft.ScreenSketch_", StringComparison.OrdinalIgnoreCase)) continue;
            using var package = packages.OpenSubKey(name);
            if (package?.GetValue("PackageRootFolder") is not string root || root.Length == 0) continue;
            var directory = Path.Combine(root, "SnippingTool");
            if (!FileNames.All(file => File.Exists(Path.Combine(directory, file)))) continue;
            if (found is null || string.Compare(directory, found, StringComparison.OrdinalIgnoreCase) > 0)
                found = directory;
        }
        return found;
    }

    private static void Require(long code, string action)
    {
        if (code != 0)
            throw new CaptureException($"剪取工具文字辨識{action}失敗（{code}）。");
    }

    private static T Export<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static float Min(float a, float b, float c, float d) => Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static float Max(float a, float b, float c, float d) => Math.Max(Math.Max(a, b), Math.Max(c, d));

    [StructLayout(LayoutKind.Sequential)]
    private struct Img
    {
        public int Type;
        public int Width;
        public int Height;
        public int Unknown;
        public long Step;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BoundingBox
    {
        public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreateInitDelegate(out long ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long SetDelayDelegate(long ctx, byte flag);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreatePipelineDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string model,
        [MarshalAs(UnmanagedType.LPStr)] string key,
        long ctx,
        out long pipeline);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CreateOptionsDelegate(out long opt);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long SetLinesDelegate(long opt, long count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long RunPipelineDelegate(long pipeline, ref Img image, long opt, out long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetCountDelegate(long instance, out long count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetAtDelegate(long instance, long index, out long item);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetTextDelegate(long item, out IntPtr text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetBoxDelegate(long item, out IntPtr box);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long GetConfidenceDelegate(long item, out float confidence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseDelegate(long result);
}
