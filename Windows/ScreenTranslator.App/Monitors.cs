using System.Runtime.InteropServices;

namespace ScreenTranslator;

internal sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public uint Dpi { get; init; }

    public (int X, int Y, int Width, int Height) ToPhysical(double dipX, double dipY, double dipWidth, double dipHeight)
    {
        var scale = Math.Max(Dpi, 96) / 96.0;
        var x = Left + (int)Math.Round(dipX * scale, MidpointRounding.AwayFromZero);
        var y = Top + (int)Math.Round(dipY * scale, MidpointRounding.AwayFromZero);
        var width = Math.Max(1, (int)Math.Round(dipWidth * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(dipHeight * scale, MidpointRounding.AwayFromZero));
        return (x, y, width, height);
    }
}

internal static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var found = new List<MonitorInfo>();
        NativeMethods.MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info) || info.rcMonitor.Width <= 0 || info.rcMonitor.Height <= 0)
                return true;
            uint dpi = 96;
            if (NativeMethods.GetDpiForMonitor(hMonitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
                dpi = dpiX;
            found.Add(new MonitorInfo
            {
                DeviceName = info.szDevice,
                Left = info.rcMonitor.Left,
                Top = info.rcMonitor.Top,
                Width = info.rcMonitor.Width,
                Height = info.rcMonitor.Height,
                Dpi = dpi,
            });
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return found;
    }

    public static MonitorInfo? Find(string deviceName)
    {
        foreach (var monitor in All())
        {
            if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return monitor;
        }
        return null;
    }
}
