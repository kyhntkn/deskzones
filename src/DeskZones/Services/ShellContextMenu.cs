using System.Runtime.InteropServices;
using System.Windows.Input;

namespace DeskZones.Services;

/// <summary>
/// Explorer'ın kendi sağ tık menüsünü (Aç, Birlikte aç, Paylaş, Kopyala, Kes, Sil,
/// Yeniden adlandır, Özellikler, Gönder, kabuk eklentileri…) gösterir ve komutları çalıştırır.
/// </summary>
public static class ShellContextMenu
{
    // ---------------- COM arayüzleri ----------------

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, IntPtr pszDisplayName, IntPtr pchEaten, IntPtr ppidl, IntPtr pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, IntPtr riid, IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, IntPtr riid, IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, IntPtr riid, IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, IntPtr rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, IntPtr pszName, uint uFlags, IntPtr ppidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    // Tüm metin alanları IntPtr: yapı blittable kalsın, sadece fiil işaretçilerini dolduruyoruz.
    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    // ---------------- Win32 ----------------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    private static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    private const uint CmdFirst = 1, CmdLast = 0x7FFF;
    private const uint CMF_NORMAL = 0x0, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    private const uint GCS_VERBW = 0x4;
    private const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_SHIFT_DOWN = 0x10000000,
                       CMIC_MASK_PTINVOKE = 0x20000000, CMIC_MASK_CONTROL_DOWN = 0x40000000;
    private const int SW_SHOWNORMAL = 1;

    private const int WM_NULL = 0x0000, WM_DRAWITEM = 0x002B, WM_MEASUREITEM = 0x002C,
                      WM_INITMENUPOPUP = 0x0117, WM_MENUCHAR = 0x0120;

    // Menü açıkken alt menülerin (Birlikte aç, Gönder…) çizilmesi için mesajlar buraya iletilir.
    private static IContextMenu2? _active2;
    private static IContextMenu3? _active3;

    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>
    /// Menüyü ekran (fiziksel piksel) konumunda açar. Seçilen komutu çalıştırır;
    /// "rename" gibi pencere tarafından yapılması gereken bir komutsa çalıştırmadan fiilini döndürür.
    /// </summary>
    public static string? Show(IntPtr hwnd, IReadOnlyList<string> paths, int x, int y)
    {
        var pidls = new List<IntPtr>();
        IContextMenu? cm = null;
        IntPtr hmenu = IntPtr.Zero;
        try
        {
            cm = GetContextMenu(hwnd, paths, pidls);
            if (cm == null) return null;

            hmenu = CreatePopupMenu();
            uint flags = CMF_NORMAL | CMF_CANRENAME;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) flags |= CMF_EXTENDEDVERBS; // Shift+sağ tık: "Yol olarak kopyala" vb.
            if (cm.QueryContextMenu(hmenu, 0, CmdFirst, CmdLast, flags) < 0) return null;

            _active3 = cm as IContextMenu3;
            _active2 = _active3 == null ? cm as IContextMenu2 : null;

            SetForegroundWindow(hwnd);
            int cmd = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            _active2 = null;
            _active3 = null;

            if (cmd < CmdFirst) return null;
            uint id = (uint)cmd - CmdFirst;
            var verb = GetVerb(cm, id);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return "rename";

            Invoke(cm, hwnd, (IntPtr)id, (IntPtr)id, x, y);
            return verb ?? "";
        }
        catch { return null; }
        finally
        {
            _active2 = null;
            _active3 = null;
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (cm != null) Marshal.ReleaseComObject(cm);
            foreach (var p in pidls) CoTaskMemFree(p);
        }
    }

    /// <summary>Menü göstermeden bir kabuk fiili çalıştırır ("delete", "copy", "cut", "properties"…).</summary>
    public static void InvokeVerb(IntPtr hwnd, IReadOnlyList<string> paths, string verb)
    {
        var pidls = new List<IntPtr>();
        IContextMenu? cm = null;
        IntPtr hmenu = IntPtr.Zero, verbA = IntPtr.Zero, verbW = IntPtr.Zero;
        try
        {
            cm = GetContextMenu(hwnd, paths, pidls);
            if (cm == null) return;
            hmenu = CreatePopupMenu();
            if (cm.QueryContextMenu(hmenu, 0, CmdFirst, CmdLast, CMF_NORMAL) < 0) return;

            verbA = Marshal.StringToHGlobalAnsi(verb);
            verbW = Marshal.StringToHGlobalUni(verb);
            var (x, y) = CursorPosition();
            Invoke(cm, hwnd, verbA, verbW, x, y);
        }
        catch { /* komut desteklenmiyorsa sessizce geç */ }
        finally
        {
            if (verbA != IntPtr.Zero) Marshal.FreeHGlobal(verbA);
            if (verbW != IntPtr.Zero) Marshal.FreeHGlobal(verbW);
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (cm != null) Marshal.ReleaseComObject(cm);
            foreach (var p in pidls) CoTaskMemFree(p);
        }
    }

    /// <summary>Pencere yordamından çağrılır; menü mesajını işlediyse true döner.</summary>
    public static bool HandleMenuMessage(int msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_active3 == null && _active2 == null) return false;
        if (msg != WM_INITMENUPOPUP && msg != WM_DRAWITEM && msg != WM_MEASUREITEM && msg != WM_MENUCHAR) return false;
        try
        {
            if (_active3 != null)
                return _active3.HandleMenuMsg2((uint)msg, wParam, lParam, out result) == 0;

            if (_active2 != null && msg != WM_MENUCHAR && _active2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
            {
                result = msg == WM_INITMENUPOPUP ? IntPtr.Zero : new IntPtr(1);
                return true;
            }
        }
        catch { }
        return false;
    }

    // ---------------- Yardımcılar ----------------

    /// <param name="paths">Aynı klasördeki öğeler olmalı (ilk öğenin üst klasörü kullanılır).</param>
    private static IContextMenu? GetContextMenu(IntPtr hwnd, IReadOnlyList<string> paths, List<IntPtr> pidls)
    {
        IShellFolder? parent = null;
        var children = new List<IntPtr>();
        try
        {
            foreach (var path in paths)
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) continue;
                pidls.Add(pidl);

                // ppidlLast, pidl'in kendi içine işaret eder; pidl serbest bırakılana kadar geçerli
                var iid = IID_IShellFolder;
                if (SHBindToParent(pidl, ref iid, out var pFolder, out var last) != 0 || pFolder == IntPtr.Zero) continue;
                try
                {
                    if (parent == null) parent = (IShellFolder)Marshal.GetObjectForIUnknown(pFolder);
                }
                finally { Marshal.Release(pFolder); }
                children.Add(last);
            }
            if (parent == null || children.Count == 0) return null;

            var iidCm = IID_IContextMenu;
            if (parent.GetUIObjectOf(hwnd, (uint)children.Count, children.ToArray(), ref iidCm, IntPtr.Zero, out var pcm) != 0
                || pcm == IntPtr.Zero)
                return null;
            try { return (IContextMenu)Marshal.GetObjectForIUnknown(pcm); }
            finally { Marshal.Release(pcm); }
        }
        finally
        {
            if (parent != null) Marshal.ReleaseComObject(parent);
        }
    }

    private static string? GetVerb(IContextMenu cm, uint id)
    {
        const int chars = 260;
        IntPtr buf = Marshal.AllocHGlobal(chars * 2);
        try
        {
            Marshal.WriteInt16(buf, 0);
            return cm.GetCommandString((UIntPtr)id, GCS_VERBW, IntPtr.Zero, buf, chars) == 0
                ? Marshal.PtrToStringUni(buf)
                : null;
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void Invoke(IContextMenu cm, IntPtr hwnd, IntPtr verbA, IntPtr verbW, int x, int y)
    {
        uint mask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE;
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Shift) != 0) mask |= CMIC_MASK_SHIFT_DOWN;     // Shift+Sil: kalıcı silme
        if ((mods & ModifierKeys.Control) != 0) mask |= CMIC_MASK_CONTROL_DOWN;

        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = mask,
            hwnd = hwnd,
            lpVerb = verbA,
            lpVerbW = verbW,
            nShow = SW_SHOWNORMAL,
            ptInvoke = new POINT { X = x, Y = y }
        };
        cm.InvokeCommand(ref info); // iptal vb. hata kodları yok sayılır
    }
}
