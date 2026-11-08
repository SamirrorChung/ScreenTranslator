using System.Runtime.CompilerServices;

namespace ScreenTranslator;

internal static class DpiBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
    }
}
