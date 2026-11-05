using System.Windows;

namespace DeskZones.Views;

public partial class TextPromptWindow : Window
{
    public string? Result { get; private set; }

    private TextPromptWindow(Window owner, string title, string label, string initial)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        LabelText.Text = label;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = Input.Text;
        DialogResult = true;
    }

    /// <summary>Kullanıcı Tamam derse metni, iptal ederse null döner.</summary>
    public static string? Ask(Window owner, string title, string label, string initial)
    {
        var dlg = new TextPromptWindow(owner, title, label, initial);
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
