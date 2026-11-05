using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace DeskZones.Services;

/// <summary>
/// "Masaüstünü göster" (görev çubuğunun sağ köşesi, Win+D) durumunu algılar.
/// Bu durumda bölmeler öne alınır, böylece gizlenmezler.
/// </summary>
public class ShowDesktopWatcher
{
    public event Action<bool>? Changed;
    public bool IsShown { get; private set; }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public ShowDesktopWatcher()
    {
        _timer.Tick += (_, _) => Check();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Check()
    {
        bool shown = Detect();
        if (shown == IsShown) return;
        IsShown = shown;
        Changed?.Invoke(shown);
    }

    private bool Detect()
    {
        int own = Environment.ProcessId;

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IsShown;
        GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid == own) return IsShown; // bölmeye tıklandıysa durumu değiştirme

        var fgClass = ClassName(fg);
        if (fgClass != "Progman" && fgClass != "WorkerW") return false;

        // Masaüstü odakta ve görünür uygulama penceresi yoksa "masaüstü gösteriliyor" demektir.
        bool anyAppWindow = false;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == own) return true;
            if (IgnoredClasses.Contains(ClassName(h))) return true;

            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            if (IsCloaked(h)) return true;
            if (GetWindowTextLength(h) == 0) return true;
            if (!GetWindowRect(h, out RECT r) || r.Right - r.Left < 100 || r.Bottom - r.Top < 50) return true;

            anyAppWindow = true;
            return false;
        }, IntPtr.Zero);

        return !anyAppWindow;
    }

    private static readonly HashSet<string> IgnoredClasses = new()
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
    };

    private static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static bool IsCloaked(IntPtr h) =>
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_APPWINDOW = 0x40000;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 13;

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
}
