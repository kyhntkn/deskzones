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

    // ---- Yüksek çözünürlüklü simge (Ctrl+tekerlek ile büyütünce bulanık olmasın) ----

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    private const int HiResSize = 128;
    private const int SIIGBF_ICONONLY = 0x4;

    private static ImageSource? LoadHiRes(string path)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory.GetImage(new SIZE { cx = HiResSize, cy = HiResSize }, SIIGBF_ICONONLY, out hbmp) != 0
                || hbmp == IntPtr.Zero)
                return null;

            int got = GetObject(hbmp, Marshal.SizeOf<DIBSECTION>(), out var ds);
            if (got != Marshal.SizeOf<DIBSECTION>() || ds.dsBm.bmBitsPixel != 32 || ds.dsBm.bmBits == IntPtr.Zero)
            {
                var plain = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                plain.Freeze();
                return plain;
            }

            int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight), stride = ds.dsBm.bmWidthBytes;
            var buf = new byte[stride * h];
            Marshal.Copy(ds.dsBm.bmBits, buf, 0, buf.Length);

            if (ds.dsBmih.biHeight > 0) // alttan yukarı DIB: satırları çevir
            {
                var row = new byte[stride];
                for (int top = 0, bottom = h - 1; top < bottom; top++, bottom--)
                {
                    Buffer.BlockCopy(buf, top * stride, row, 0, stride);
                    Buffer.BlockCopy(buf, bottom * stride, buf, top * stride, stride);
                    Buffer.BlockCopy(row, 0, buf, bottom * stride, stride);
                }
            }

            bool hasAlpha = false;
            for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { hasAlpha = true; break; }

            var src = BitmapSource.Create(w, h, 96, 96,
                hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, buf, stride);
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    private static ImageSource? Load(string path) => LoadHiRes(path) ?? LoadSmall(path);

    private static ImageSource? LoadSmall(string path)
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
