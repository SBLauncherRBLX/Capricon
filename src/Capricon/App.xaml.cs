using System.Windows;
using Capricon.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Capricon;

public partial class App : Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;
    MainWindow? _capri;
    SettingsWindow? _settingsWindow;
    AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One Capri per desktop.
        _single = new Mutex(true, "Capricon.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _capri?.Say(new Core.Reply($"Ой, сбой: {args.Exception.Message}", Core.Mood.Sad));
            args.Handled = true;
        };

        _settings = AppSettings.Load();
        _capri = new MainWindow(_settings);
        _capri.SettingsRequested += OpenSettings;
        _capri.TimerNotification += label =>
            _tray?.ShowBalloonTip(5000, "Капри", label.Length > 0 ? $"Таймер: {label}" : "Таймер сработал!", Forms.ToolTipIcon.Info);
        _capri.Show();
        CreateTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _single?.Dispose();
        base.OnExit(e);
    }

    void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Поговорить (Ctrl+Alt+C)", null, (_, _) => _capri?.OpenChat());
        menu.Items.Add("Показать / спрятать (Ctrl+Alt+H)", null, (_, _) => _capri?.ToggleVisible());
        var mute = new Forms.ToolStripMenuItem("Без голоса") { CheckOnClick = true, Checked = _settings.Muted };
        mute.CheckedChanged += (_, _) => _capri?.SetMuted(mute.Checked);
        menu.Items.Add(mute);
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIcon(),
            Text = "Капри",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => _capri?.ToggleVisible();
    }

    void OpenSettings()
    {
        if (_capri is null) return;
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _capri.Voices);
        _settingsWindow.Saved += () => _capri.ApplySettings();
        _settingsWindow.Show();
    }

    /// <summary>Tiny Capri head for the tray: pink mane, black face, green mask, white grin.</summary>
    static Drawing.Icon TrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var pink = Drawing.Color.FromArgb(255, 45, 126);
            Drawing.PointF[] mane = [new(2, 14), new(6, 3), new(12, 8), new(16, 0), new(21, 7), new(29, 3), new(27, 13), new(31, 22), new(22, 20), new(10, 22), new(1, 24)];
            using (var b = new Drawing.SolidBrush(pink)) g.FillPolygon(b, mane);
            using (var b = new Drawing.SolidBrush(Drawing.Color.FromArgb(10, 10, 18))) g.FillPolygon(b, new Drawing.PointF[] { new(9, 12), new(23, 11), new(24, 20), new(17, 30), new(9, 22) });
            using (var b = new Drawing.SolidBrush(Drawing.Color.FromArgb(29, 255, 134))) g.FillPolygon(b, new Drawing.PointF[] { new(8, 13), new(11, 8), new(14, 12), new(18, 7), new(20, 11), new(25, 9), new(25, 17), new(8, 18) });
            using (var b = new Drawing.SolidBrush(Drawing.Color.White)) g.FillPie(b, 12, 17, 9, 9, 0, 180);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
