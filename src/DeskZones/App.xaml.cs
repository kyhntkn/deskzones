using System.Windows;
using DeskZones.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DeskZones;

public partial class App : Application
{
    private Mutex? _mutex;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _toggleIcons;
    private FenceManager? _manager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "DeskZones.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }

        DispatcherUnhandledException += (_, args) =>
        {
            DesktopService.SetIconsVisible(true); // hata olsa bile masaüstü simgeleri kaybolmasın
            MessageBox.Show(args.Exception.Message, "DeskZones");
            args.Handled = true;
        };

        _manager = new FenceManager();
        _manager.Start();
        BuildTray();
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Yeni bölme", null, (_, _) => _manager!.NewFence());
        menu.Items.Add("Bölmeleri hizala", null, (_, _) => _manager!.ArrangeAll());

        _toggleIcons = new Forms.ToolStripMenuItem("", null, (_, _) =>
        {
            _manager!.SetIconsHidden(!_manager.IconsHidden);
            RefreshToggleText();
        });
        menu.Items.Add(_toggleIcons);
        RefreshToggleText();

        if (!StartupHelper.IsPackaged)
        {
            var startup = new Forms.ToolStripMenuItem("Windows ile başlat")
            {
                CheckOnClick = true,
                Checked = StartupHelper.IsEnabled
            };
            startup.CheckedChanged += (_, _) => StartupHelper.Set(startup.Checked);
            menu.Items.Add(startup);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());

        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico")).Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new Drawing.Icon(stream),
            Text = "DeskZones",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void RefreshToggleText()
    {
        if (_toggleIcons != null && _manager != null)
            _toggleIcons.Text = _manager.IconsHidden
                ? "Masaüstü simgelerini göster"
                : "Masaüstü simgelerini gizle";
    }

    private void ExitApp()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _manager?.Shutdown();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _manager?.Shutdown();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _manager?.Shutdown();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
