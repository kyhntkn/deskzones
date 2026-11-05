using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskZones.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attrs, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_DISPLAYNAME = 0x200;
    private const uint SHGFI_PIDL = 0x8;

    /// <summary>Sanal öğelerin (Bu Bilgisayar vb.) Windows dilindeki adı.</summary>
    public static string GetDisplayName(string shellPath)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(shellPath, IntPtr.Zero, out pidl, 0, out _) != 0) return shellPath;
            var info = new SHFILEINFO();
            SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_DISPLAYNAME | SHGFI_PIDL);
            return string.IsNullOrEmpty(info.szDisplayName) ? shellPath : info.szDisplayName;
        }
        catch { return shellPath; }
        finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
    }

    public static ImageSource? GetIcon(string path)
    {
        // .exe/.lnk gibi dosyalarda ikon değişebilir; önbellek yolu anahtar alır.
        return Cache.GetOrAdd(path, Load);
    }

    public static void Forget(string path) => Cache.TryRemove(path, out _);

    private static ImageSource? Load(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            IntPtr result;
            if (VirtualItems.IsVirtual(path))
            {
                IntPtr pidl = IntPtr.Zero;
                try
                {
                    if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0) return null;
                    result = SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf(info),
                        SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                }
                finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
            }
            else
            {
                result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
            }
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }
}
