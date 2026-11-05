using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DeskZones.Models;
using DeskZones.Services;

namespace DeskZones.Views;

public partial class FenceWindow : Window
{
    private const string DragFormat = "DeskZones.Item";

    // Izgara ölçüleri (XAML ile uyumlu): hücre 88x88, kenar boşlukları 10 / 44
    public const double CellW = 88, CellH = 88, ChromeW = 10, ChromeH = 44, Grid = 20;

    public static double SnapWidth(double w) =>
        ChromeW + Math.Max(2, Math.Round((w - ChromeW) / CellW)) * CellW;

    public static double SnapHeight(double h) =>
        ChromeH + Math.Max(1, Math.Round((h - ChromeH) / CellH)) * CellH;

    private readonly FenceManager _manager;
    private Point _dragStart;
    private FenceItem? _dragItem;

    public FenceConfig Config { get; }
    public ObservableCollection<FenceItem> Items { get; } = new();

    /// <summary>true olmadıkça pencere kapatılamaz (Alt+F4 vb. engellenir).</summary>
    public bool ForceClose { get; set; }

    public FenceWindow(FenceConfig config, FenceManager manager)
    {
        InitializeComponent();
        Config = config;
        _manager = manager;

        // Kayıtlı düzensiz boyutları da ızgaraya oturt
        Left = Math.Round(config.Left / Grid) * Grid;
        Top = Math.Round(config.Top / Grid) * Grid;
        Width = SnapWidth(config.Width);
        Height = SnapHeight(config.Height);

        foreach (var p in config.Items) Items.Add(new FenceItem(p));
        ItemsList.ItemsSource = Items;
        RefreshTitle();

        LocationChanged += (_, _) => _manager.ScheduleSave();
        SizeChanged += (_, _) => _manager.ScheduleSave();
        Activated += (_, _) => SendToBottom();
        StateChanged += (_, _) =>
        {
            // "Masaüstünü göster" küçültürse geri getir
            if (WindowState == WindowState.Minimized)
                Dispatcher.BeginInvoke(new Action(() => { WindowState = WindowState.Normal; }));
        };
    }

    // ---------------- Izgara / hizalama ----------------

    private const int WM_EXITSIZEMOVE = 0x0232;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE) SnapToGrid(); // taşıma/boyutlandırma bitince
        return IntPtr.Zero;
    }

    private void SnapToGrid()
    {
        Width = SnapWidth(Width);
        Height = SnapHeight(Height);
        Left = Math.Round(Left / Grid) * Grid;
        Top = Math.Round(Top / Grid) * Grid;
        _manager.ScheduleSave();
    }

    public void ApplyGeometry(double left, double top, double width, double height)
    {
        Left = left; Top = top; Width = width; Height = height;
        _manager.ScheduleSave();
    }

    /// <summary>"Masaüstünü göster" aktifken bölmeleri öne alır, bitince tekrar alta indirir.</summary>
    public void SetDesktopShown(bool shown)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = shown;
        if (!shown) SendToBottom();
    }

    // ---------------- Başlık ----------------

    public void RefreshTitle() => TitleText.Text = Config.Title + (Config.IsDefault ? "  ★" : "");

    public void SyncGeometry()
    {
        if (WindowState != WindowState.Normal) return;
        Config.Left = Left;
        Config.Top = Top;
        Config.Width = Width;
        Config.Height = Height;
    }

    private void BeginRename()
    {
        TitleBox.Text = Config.Title;
        TitleBox.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Collapsed;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void CommitRename(bool save)
    {
        if (TitleBox.Visibility != Visibility.Visible) return;
        if (save && !string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            Config.Title = TitleBox.Text.Trim();
            _manager.ScheduleSave();
        }
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        RefreshTitle();
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitRename(true);
        else if (e.Key == Key.Escape) CommitRename(false);
    }

    private void TitleBox_LostFocus(object sender, RoutedEventArgs e) => CommitRename(true);

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TitleBox.Visibility == Visibility.Visible) return;
        if (e.ClickCount == 2) { BeginRename(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ---------------- Öğe yönetimi ----------------

    public bool Contains(string path) =>
        Items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));

    /// <param name="index">Eklenecek konum; negatifse sona eklenir.</param>
    public void AddItem(string path, int index = -1)
    {
        if (Contains(path)) return;
        var item = new FenceItem(path);
        if (index < 0 || index > Items.Count)
        {
            Items.Add(item);
            Config.Items.Add(path);
        }
        else
        {
            Items.Insert(index, item);
            Config.Items.Insert(index, path);
        }
    }

    /// <summary>Bölme içinde öğeyi yeni sıraya taşır; index negatifse sona alır.</summary>
    public void ReorderItem(string path, int index)
    {
        int old = Items.ToList().FindIndex(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (old < 0) return;
        if (index < 0 || index >= Items.Count) index = Items.Count - 1;
        if (index == old) return;

        Items.Move(old, index);
        var p = Config.Items[old];
        Config.Items.RemoveAt(old);
        Config.Items.Insert(index, p);
    }

    /// <summary>Bırakılan noktanın altındaki öğenin sırası; öğe yoksa -1.</summary>
    private int DropIndex(DragEventArgs e)
    {
        var hit = ItemsList.InputHitTest(e.GetPosition(ItemsList)) as DependencyObject;
        var target = ItemAt(hit);
        return target == null ? -1 : Items.IndexOf(target);
    }

    public void RemoveItem(string path)
    {
        var item = Items.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (item != null) Items.Remove(item);
        Config.Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }

    public void ReplaceItem(string oldPath, string newPath)
    {
        var item = Items.FirstOrDefault(i => string.Equals(i.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));
        if (item == null) { AddItem(newPath); return; }
        int index = Items.IndexOf(item);
        Items[index] = new FenceItem(newPath);
        int cfgIndex = Config.Items.FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
        if (cfgIndex >= 0) Config.Items[cfgIndex] = newPath;
    }

    private FenceItem? ItemAt(object? source)
    {
        if (source is not DependencyObject d) return null;
        var container = ItemsControl.ContainerFromElement(ItemsList, d) as ListBoxItem;
        return container?.DataContext as FenceItem;
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "DeskZones"); }
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is { } item) Open(item.FullPath);
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is FenceItem item) Open(item.FullPath);
    }

    private void ShowItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is FenceItem { IsVirtual: false } item)
            Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
    }

    private void ItemsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemAt(e.OriginalSource);
        if (item != null) ItemsList.SelectedItem = item;
        else ItemsList.SelectedItem = null;
    }

    private void ItemsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Boş alana sağ tıklanırsa öğe menüsü yerine bölme menüsünü göster.
        if (ItemsList.SelectedItem == null)
        {
            e.Handled = true;
            FenceMenu.PlacementTarget = ItemsList;
            FenceMenu.IsOpen = true;
        }
    }

    // ---------------- Sürükle & bırak ----------------

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = ItemAt(e.OriginalSource);
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragItem;
        _dragItem = null;

        var data = new DataObject();
        data.SetData(DragFormat, $"{Config.Id}|{item.FullPath}");
        if (!item.IsVirtual) data.SetData(DataFormats.FileDrop, new[] { item.FullPath });
        DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        int index = DropIndex(e);

        if (e.Data.GetDataPresent(DragFormat) && e.Data.GetData(DragFormat) is string payload)
        {
            var parts = payload.Split('|', 2);
            if (parts.Length == 2)
            {
                if (parts[0] == Config.Id)
                {
                    ReorderItem(parts[1], index);   // aynı bölme: sırayı değiştir
                    _manager.ScheduleSave();
                }
                else
                {
                    _manager.MoveItem(parts[1], parts[0], this, index);
                }
            }
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) _manager.AssignToFence(f, this, index);
        }
        e.Handled = true;
    }

    // ---------------- Menü komutları ----------------

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRename();
    private void Rules_Click(object sender, RoutedEventArgs e) => _manager.EditRules(this);
    private void Default_Click(object sender, RoutedEventArgs e) => _manager.SetDefault(this);
    private void NewFence_Click(object sender, RoutedEventArgs e) => _manager.NewFence();
    private void Delete_Click(object sender, RoutedEventArgs e) => _manager.DeleteFence(this);
    private void Arrange_Click(object sender, RoutedEventArgs e) => _manager.ArrangeAll();

    // ---------------- Masaüstü katmanında tutma ----------------

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Alt+Tab listesinde görünmesin
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        SendToBottom();
    }

    private void SendToBottom()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ForceClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
