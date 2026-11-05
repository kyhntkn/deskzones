using System.Windows;
using System.Windows.Threading;
using DeskZones.Models;
using DeskZones.Views;

namespace DeskZones.Services;

/// <summary>Tüm bölmeleri, ayarları ve masaüstü izlemeyi yöneten merkez.</summary>
public class FenceManager
{
    private AppConfig _config = new();
    private readonly List<FenceWindow> _windows = new();
    private readonly DesktopService _desktop = new();
    private readonly ShowDesktopWatcher _showDesktop = new();
    private readonly DispatcherTimer _saveTimer;
    private bool _shutDown;

    public bool IconsHidden => _config.IconsHidden;

    public FenceManager()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    // ---------------- Başlatma / kapatma ----------------

    public void Start()
    {
        _config = ConfigService.Load();
        if (_config.Fences.Count == 0) CreateDefaultFences();
        if (!_config.Fences.Any(f => f.IsDefault)) _config.Fences[^1].IsDefault = true;

        Reconcile();
        foreach (var cfg in _config.Fences) OpenWindow(cfg);

        DesktopService.SetIconsVisible(!_config.IconsHidden);

        _desktop.Created += p => Ui(() => OnCreated(p));
        _desktop.Deleted += p => Ui(() => OnDeleted(p));
        _desktop.Renamed += (o, n) => Ui(() => OnRenamed(o, n));
        _desktop.Start();

        _showDesktop.Changed += shown => { foreach (var w in _windows) w.SetDesktopShown(shown); };
        _showDesktop.Start();

        SaveNow();
    }

    public void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        _saveTimer.Stop();
        _showDesktop.Stop();
        SaveNow();
        _desktop.Dispose();
        DesktopService.SetIconsVisible(true); // çıkarken masaüstü simgelerini geri getir
        foreach (var w in _windows.ToList()) { w.ForceClose = true; w.Close(); }
    }

    private static void Ui(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

    // ---------------- Kaydetme ----------------

    public void ScheduleSave()
    {
        if (_shutDown) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        foreach (var w in _windows) w.SyncGeometry();
        ConfigService.Save(_config);
    }

    // ---------------- İlk kurulum ve eşitleme ----------------

    private void CreateDefaultFences()
    {
        var wa = SystemParameters.WorkArea;
        const double margin = 20;
        double slot = (wa.Width - margin * 4) / 3;
        int cols = Math.Max(2, (int)Math.Floor((slot - FenceWindow.ChromeW) / FenceWindow.CellW));
        double w = FenceWindow.ChromeW + cols * FenceWindow.CellW;
        double h = FenceWindow.ChromeH + 3 * FenceWindow.CellH;

        FenceConfig Make(string title, double index, params string[] rules) => new()
        {
            Title = title,
            Left = wa.Left + margin + index * (w + margin),
            Top = wa.Top + margin,
            Width = w,
            Height = h,
            Rules = rules.ToList()
        };

        _config.Fences.Add(Make("Programlar", 0, "[sistem]", ".lnk", ".exe", ".url", ".msi", ".bat"));
        _config.Fences.Add(Make("Klasörler", 1, "[klasör]"));
        var docs = Make("Dosyalar ve Belgeler", 2);
        docs.IsDefault = true;
        _config.Fences.Add(docs);
    }

    /// <summary>Masaüstündeki gerçek durumla ayarları eşitler (silinenleri çıkarır, yenileri yerleştirir).</summary>
    private void Reconcile()
    {
        var onDesktop = DesktopService.EnumerateItems().ToList();
        var existing = new HashSet<string>(onDesktop, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in _config.Fences)
            f.Items = f.Items.Where(p => (VirtualItems.IsVirtual(p) || existing.Contains(p)) && seen.Add(p)).ToList();

        foreach (var p in onDesktop.Concat(VirtualItems.Defaults))
            if (!seen.Contains(p)) PickFence(p).Items.Add(p);
    }

    // ---------------- Kurallar ----------------

    public FenceConfig PickFence(string path)
    {
        if (VirtualItems.IsVirtual(path))
        {
            var sys = _config.Fences.FirstOrDefault(f => f.Rules.Contains("[sistem]"));
            if (sys != null) return sys;
        }
        bool isDir = System.IO.Directory.Exists(path);
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

        foreach (var f in _config.Fences)
        {
            foreach (var rule in f.Rules)
            {
                if (isDir && rule == "[klasör]") return f;
                if (!isDir && rule.StartsWith('.') && rule == ext) return f;
            }
        }
        return _config.Fences.FirstOrDefault(f => f.IsDefault) ?? _config.Fences[0];
    }

    public static List<string> ParseRules(string text) =>
        text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim().ToLowerInvariant())
            .Select(r => r == "[klasor]" ? "[klasör]" : r == "[sistem]" ? "[sistem]" : r)
            .Select(r => r.StartsWith('[') || r.StartsWith('.') ? r : "." + r)
            .Distinct()
            .ToList();

    public void EditRules(FenceWindow w)
    {
        var answer = TextPromptWindow.Ask(w, "Otomatik yerleştirme kuralları",
            "Bu bölmeye otomatik girecek dosya türleri (virgülle ayır).\nÖrnek: .pdf, .docx, .xlsx   — klasörler için: [klasör]",
            string.Join(", ", w.Config.Rules));
        if (answer == null) return;
        w.Config.Rules = ParseRules(answer);
        ScheduleSave();
    }

    // ---------------- Pencereler ----------------

    private FenceWindow GetWindow(FenceConfig cfg) => _windows.First(w => w.Config == cfg);

    private FenceWindow? WindowContaining(string path) => _windows.FirstOrDefault(w => w.Contains(path));

    private void OpenWindow(FenceConfig cfg)
    {
        ClampToScreen(cfg);
        var win = new FenceWindow(cfg, this);
        _windows.Add(win);
        win.Show();
        win.SetDesktopShown(_showDesktop.IsShown);
    }

    private static void ClampToScreen(FenceConfig c)
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        if (c.Left + c.Width < vl + 60 || c.Left > vr - 60 || c.Top + 40 < vt || c.Top > vb - 60)
        {
            c.Left = vl + 100;
            c.Top = vt + 100;
        }
    }

    public void NewFence()
    {
        var wa = SystemParameters.WorkArea;
        var cfg = new FenceConfig { Left = wa.Left + 120, Top = wa.Top + 120 };
        _config.Fences.Add(cfg);
        OpenWindow(cfg);
        ScheduleSave();
    }

    public void DeleteFence(FenceWindow w)
    {
        if (_windows.Count <= 1)
        {
            MessageBox.Show("En az bir bölme kalmalı.", "DeskZones", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (w.Config.IsDefault)
        {
            var other = _windows.First(x => x != w);
            SetDefault(other);
        }

        var target = GetWindow(_config.Fences.First(f => f.IsDefault));
        foreach (var item in w.Items.ToList())
        {
            w.RemoveItem(item.FullPath);
            target.AddItem(item.FullPath);
        }

        _windows.Remove(w);
        _config.Fences.Remove(w.Config);
        w.ForceClose = true;
        w.Close();
        ScheduleSave();
    }

    /// <summary>Tüm bölmeleri aynı boyuta getirip düzgün bir ızgarada dizer.</summary>
    public void ArrangeAll()
    {
        if (_windows.Count == 0) return;
        var wa = SystemParameters.WorkArea;
        const double gap = 20;

        double w = Math.Max(FenceWindow.SnapWidth(_windows.Max(x => x.Width)), FenceWindow.ChromeW + 4 * FenceWindow.CellW);
        double h = Math.Max(FenceWindow.SnapHeight(_windows.Max(x => x.Height)), FenceWindow.ChromeH + 3 * FenceWindow.CellH);

        double x0 = wa.Left + gap, x = x0, y = wa.Top + gap;
        foreach (var win in _windows)
        {
            if (x > x0 && x + w > wa.Right - gap) { x = x0; y += h + gap; }
            win.ApplyGeometry(x, y, w, h);
            x += w + gap;
        }
    }

    public void SetDefault(FenceWindow w)
    {
        foreach (var f in _config.Fences) f.IsDefault = f == w.Config;
        foreach (var win in _windows) win.RefreshTitle();
        ScheduleSave();
    }

    public void SetIconsHidden(bool hidden)
    {
        _config.IconsHidden = hidden;
        DesktopService.SetIconsVisible(!hidden);
        ScheduleSave();
    }

    // ---------------- Öğe taşıma ----------------

    public void MoveItem(string path, string fromFenceId, FenceWindow to, int index = -1)
    {
        var from = _windows.FirstOrDefault(w => w.Config.Id == fromFenceId);
        if (from == to) return;
        from?.RemoveItem(path);
        to.AddItem(path, index);
        ScheduleSave();
    }

    /// <summary>Dışarıdan (Explorer vb.) bırakılan masaüstü öğesini bu bölmeye al.</summary>
    public void AssignToFence(string path, FenceWindow to, int index = -1)
    {
        if (!DesktopService.IsOnDesktop(path)) return;
        WindowContaining(path)?.RemoveItem(path);
        to.AddItem(path, index);
        ScheduleSave();
    }

    // ---------------- Masaüstü olayları ----------------

    private void OnCreated(string path)
    {
        if (!DesktopService.IsUsable(path) || WindowContaining(path) != null) return;
        GetWindow(PickFence(path)).AddItem(path);
        ScheduleSave();
    }

    private void OnDeleted(string path)
    {
        WindowContaining(path)?.RemoveItem(path);
        IconService.Forget(path);
        ScheduleSave();
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        var win = WindowContaining(oldPath);
        if (win == null) { OnCreated(newPath); return; }
        IconService.Forget(oldPath);
        win.ReplaceItem(oldPath, newPath);
        ScheduleSave();
    }
}
