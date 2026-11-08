using System.Windows;

namespace ScreenTranslator;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Thread? _showThread;
    private TrayController? _tray;
    private volatile bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _showEvent = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\ScreenTranslator.Show");
        _mutex = new Mutex(false, @"Local\ScreenTranslator.SingleInstance");
        var ownsMutex = false;
        try
        {
            ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        var model = new AppModel();
        model.Bootstrap();
        _tray = new TrayController(model);
        _showThread = new Thread(WaitForShow)
        {
            IsBackground = true,
            Name = "ShowSettings",
        };
        _showThread.Start();
        _tray.ShowSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _showEvent?.Set();
        _showThread?.Join(TimeSpan.FromMilliseconds(300));
        _tray?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            _mutex.Dispose();
        }
        _showEvent?.Dispose();
        base.OnExit(e);
    }

    private void WaitForShow()
    {
        if (_showEvent is null) return;
        while (!_exiting)
        {
            _showEvent.WaitOne();
            if (_exiting) return;
            _showEvent.Reset();
            try
            {
                Dispatcher.Invoke(() => _tray?.ShowSettings());
            }
            catch (Exception)
            {
                return;
            }
        }
    }
}
