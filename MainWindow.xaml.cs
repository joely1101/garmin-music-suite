using System.Windows;
using GarminMusicStudio.ViewModels;

namespace GarminMusicStudio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
