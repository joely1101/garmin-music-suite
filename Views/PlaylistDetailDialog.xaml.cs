using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Views;

public partial class PlaylistDetailDialog : Window
{
    public PlaylistDetailDialog(string playlistName, List<PlaylistEntry> entries)
    {
        InitializeComponent();
        TxtPlaylistName.Text = playlistName;
        TxtInfo.Text = $"共 {entries.Count} 首歌曲";
        ItemsSongs.ItemsSource = entries;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
