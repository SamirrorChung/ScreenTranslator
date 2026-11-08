using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace ScreenTranslator;

internal sealed class TrayController : IDisposable
{
    private readonly AppModel _model;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _regionItem;
    private readonly ToolStripMenuItem _toggleItem;
    private SettingsWindow? _settings;
    private IntPtr _iconHandle;

    public TrayController(AppModel model)
    {
        _model = model;
        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "螢幕翻譯",
            Visible = true,
        };
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _regionItem = new ToolStripMenuItem { Enabled = false };
        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => _model.ToggleRunning();
        var selectItem = new ToolStripMenuItem("圈選區域");
        selectItem.Click += (_, _) => _model.SelectRegion();
        var logItem = new ToolStripMenuItem("紀錄…");
        logItem.Click += (_, _) => LogWindow.ShowLatest();
        var settingsItem = new ToolStripMenuItem("設定…");
        settingsItem.Click += (_, _) => ShowSettings();
        var quitItem = new ToolStripMenuItem("結束");
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_regionItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(selectItem);
        menu.Items.Add(logItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
        menu.Opening += (_, _) => RefreshMenu();
        _icon.ContextMenuStrip = menu;
        _icon.MouseUp += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_icon, null);
        };
        RefreshMenu();
    }

    public void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(_model);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        _settings.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        if (_iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
        _icon.Dispose();
        _settings?.Close();
        LogWindow.CloseLatest();
    }

    private void RefreshMenu()
    {
        _statusItem.Text = _model.Status;
        _regionItem.Text = _model.RegionDescription;
        _toggleItem.Text = _model.IsRunning ? "暫停" : "開始";
    }

    private Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(32, 32, 32));
            graphics.FillEllipse(brush, 2, 3, 28, 20);
            graphics.FillPolygon(brush, new[] { new Point(11, 20), new Point(9, 29), new Point(18, 20) });
            using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            graphics.DrawString("文", font, white, 7, 5);
        }
        _iconHandle = bitmap.GetHicon();
        return Icon.FromHandle(_iconHandle);
    }
}
