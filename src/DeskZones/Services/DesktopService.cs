using System.IO;
using System.Runtime.InteropServices;

namespace DeskZones.Services;

/// <summary>Masaüstü klasörlerini izler ve Explorer'ın masaüstü simgelerini gizler/gösterir.</summary>
public class DesktopService : IDisposable
{
    public event Action<string>? Created;
    public event Action<string>? Deleted;
    public event Action<string, string>? Renamed;

    private readonly List<FileSystemWatcher> _watchers = new();

    public static IEnumerable<string> DesktopDirs =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }
        .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> EnumerateItems()
    {
        foreach (var dir in DesktopDirs)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
            catch { continue; }
            foreach (var e in entries)
                if (IsUsable(e)) yield return e;
        }
    }

    /// <summary>Gizli/sistem dosyalarını (desktop.ini gibi) atlar.</summary>
    public static bool IsUsable(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }
        catch { return false; }
    }

    public static bool IsOnDesktop(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        return dir != null && DesktopDirs.Any(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase));
    }

    public void Start()
    {
        foreach (var dir in DesktopDirs)
        {
            var w = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            w.Created += (_, e) => Created?.Invoke(e.FullPath);
            w.Deleted += (_, e) => Deleted?.Invoke(e.FullPath);
            w.Renamed += (_, e) => Renamed?.Invoke(e.OldFullPath, e.FullPath);
            _watchers.Add(w);
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    // ---- Masaüstü simgelerini gizle / göster (Explorer'ın "Simgeleri göster" komutu) ----

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string cls, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private const uint WM_COMMAND = 0x0111;
    private const int ToggleDesktopIcons = 0x7402;

    private static IntPtr GetDefView()
    {
        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero) return defView;

        var worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        return IntPtr.Zero;
    }

    public static bool AreIconsVisible()
    {
        var defView = GetDefView();
        if (defView == IntPtr.Zero) return true;
        var list = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return list == IntPtr.Zero || IsWindowVisible(list);
    }

    public static void SetIconsVisible(bool visible)
    {
        if (AreIconsVisible() == visible) return;
        var defView = GetDefView();
        if (defView != IntPtr.Zero)
            SendMessage(defView, WM_COMMAND, (IntPtr)ToggleDesktopIcons, IntPtr.Zero);
    }
}
