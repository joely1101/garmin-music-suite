using System.Windows;
using System.Windows.Input;

namespace GarminMusicStudio.Views;

public partial class InputDialog : Window
{
    public string InputText => TxtInput.Text;

    public InputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        
        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;
        TxtInput.Text = defaultText;
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
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
