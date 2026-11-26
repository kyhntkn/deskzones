using System.Windows;

namespace DeskZones.Views;

public partial class TextPromptWindow : Window
{
    public string? Result { get; private set; }

    private TextPromptWindow(Window owner, string title, string label, string initial, int selectLength)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        LabelText.Text = label;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            if (selectLength >= 0 && selectLength <= initial.Length) Input.Select(0, selectLength);
            else Input.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = Input.Text;
        DialogResult = true;
    }

    /// <summary>Kullanıcı Tamam derse metni, iptal ederse null döner.</summary>
    /// <param name="selectLength">Baştan seçilecek karakter sayısı (örn. uzantısız ad); negatifse tümü.</param>
    public static string? Ask(Window owner, string title, string label, string initial, int selectLength = -1)
    {
        var dlg = new TextPromptWindow(owner, title, label, initial, selectLength);
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
