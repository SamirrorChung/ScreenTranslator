using System.Text.Json.Serialization;

namespace ScreenTranslator;

public sealed class CaptureRegion
{
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonIgnore]
    public string Summary =>
        $"已圈選 {(int)Math.Round(Width, MidpointRounding.AwayFromZero)} × {(int)Math.Round(Height, MidpointRounding.AwayFromZero)}";
}

internal sealed class CaptureException : Exception
{
    public CaptureException(string message) : base(message)
    {
    }
}
