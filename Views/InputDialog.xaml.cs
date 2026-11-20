using System.Windows;

namespace GarminMusicStudio.Views;

public partial class InputDialog : Window
{
    public string InputText => TxtInput.Text;

    public InputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;
        TxtInput.Text = defaultText;
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
