using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class WatchSyncViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string? _currentFolder;
    private string _searchText = string.Empty;
    private bool _isSelectAll;
    private bool _isSyncing;
    private string _syncStatusText = string.Empty;
    private Track? _playingTrack;
    private MediaPlayer? _player;

    public ObservableCollection<Track> LocalTracks { get; } = new();
    public ObservableCollection<WatchTrack> WatchTracks { get; } = new();
    public ObservableCollection<WatchPlaylist> WatchPlaylists { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                _ = LoadLocalTracksAsync();
            }
        }
    }

    public bool IsSelectAll
    {
        get => _isSelectAll;
        set
        {
            if (SetField(ref _isSelectAll, value))
            {
                foreach (var t in LocalTracks) t.IsSelected = value;
            }
        }
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetField(ref _isSyncing, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        set => SetField(ref _syncStatusText, value);
    }

    public Track? PlayingTrack
    {
        get => _playingTrack;
        set => SetField(ref _playingTrack, value);
    }

    public ICommand ToggleSelectAllCommand { get; }
    public ICommand SyncSelectedCommand { get; }
    public ICommand DeleteTrackCommand { get; }
    public ICommand RenameTrackCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand DeleteWatchTrackCommand { get; }
    public ICommand CreatePlaylistCommand { get; }

    public WatchSyncViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;

        ToggleSelectAllCommand = new RelayCommand(() => IsSelectAll = !IsSelectAll);
        SyncSelectedCommand = new AsyncRelayCommand(SyncSelectedAsync);
        DeleteTrackCommand = new AsyncRelayCommand(DeleteLocalTrackAsync);
        RenameTrackCommand = new RelayCommand(RenameLocalTrack);
        PlayTrackCommand = new RelayCommand(TogglePlayTrack);
        DeleteWatchTrackCommand = new AsyncRelayCommand(DeleteWatchTrackItemAsync);
        CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistFromSelectedAsync);
    }

    public void FilterByFolder(string? folder)
    {
        _currentFolder = folder;
        _ = LoadLocalTracksAsync();
    }

    public async Task LoadLocalTracksAsync()
    {
        var tracks = await DatabaseService.Instance.GetTracksAsync(_currentFolder, SearchText);
        LocalTracks.Clear();
        foreach (var t in tracks) LocalTracks.Add(t);
    }

    public async Task LoadWatchTracksAsync(WatchDevice device)
    {
        var list = await MtpService.Instance.GetWatchTracksAsync(device);
        WatchTracks.Clear();
        foreach (var wt in list) WatchTracks.Add(wt);
    }

    private async Task SyncSelectedAsync()
    {
        var selected = LocalTracks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("請先勾選欲同步至手錶的歌曲！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_mainVM.Watch.IsConnected)
        {
            MessageBox.Show("未偵測到連線中的 Garmin 手錶！請確認手錶已以 USB 連接電腦。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsSyncing = true;
        try
        {
            int current = 0;
            int total = selected.Count;

            foreach (var t in selected)
            {
                current++;
                SyncStatusText = $"同步中 ({current}/{total}): {t.Title}...";
                await MtpService.Instance.SyncTrackToWatchAsync(_mainVM.Watch, t);
            }

            SyncStatusText = "同步完成！";
            await _mainVM.RefreshWatchStatusAsync();
            MessageBox.Show($"已成功同步 {total} 首歌曲至手錶！", "同步成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"同步失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSyncing = false;
            SyncStatusText = string.Empty;
        }
    }

    private async Task DeleteLocalTrackAsync(object? param)
    {
        if (param is Track track)
        {
            if (MessageBox.Show($"確定要從電腦刪除歌曲「{track.Title}」嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await DatabaseService.Instance.DeleteTrackAsync(track.Id);
                LocalTracks.Remove(track);
                await _mainVM.RefreshFoldersAsync();
            }
        }
    }

    private void RenameLocalTrack(object? param)
    {
        if (param is Track track)
        {
            var dialog = new Views.InputDialog("重新命名歌曲", "請輸入新的歌曲標題:", track.Title);
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                track.Title = dialog.InputText.Trim();
                _ = DatabaseService.Instance.RenameTrackAsync(track.Id, track.Title, track.Artist);
            }
        }
    }

    private void TogglePlayTrack(object? param)
    {
        if (param is Track track)
        {
            if (PlayingTrack == track)
            {
                _player?.Stop();
                PlayingTrack = null;
                return;
            }

            var fullPath = Path.Combine(DatabaseService.Instance.MusicDir, track.RelPath.Replace('/', '\\'));
            if (!File.Exists(fullPath)) return;

            try
            {
                _player?.Stop();
                _player = new MediaPlayer();
                _player.Open(new Uri(fullPath));
                _player.Play();
                PlayingTrack = track;
            }
            catch { }
        }
    }

    private async Task DeleteWatchTrackItemAsync(object? param)
    {
        if (param is WatchTrack wt)
        {
            if (MessageBox.Show($"確定要從手錶中刪除歌曲「{wt.Title}」嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var ok = await MtpService.Instance.DeleteWatchTrackAsync(_mainVM.Watch, wt.RelPath);
                if (ok)
                {
                    WatchTracks.Remove(wt);
                    await _mainVM.RefreshWatchStatusAsync();
                }
                else
                {
                    MessageBox.Show("刪除失敗，請確認手錶連線。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async Task CreatePlaylistFromSelectedAsync()
    {
        var selected = LocalTracks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("請先勾選欲加入播放清單的歌曲！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.InputDialog("手錶播放清單", "請輸入播放清單名稱 (例如: 跑步訓練):");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var name = dialog.InputText.Trim();
            var relPaths = selected.Select(t => t.RelPath).ToList();
            var ok = await MtpService.Instance.SaveWatchPlaylistAsync(_mainVM.Watch, name, relPaths);
            if (ok)
            {
                MessageBox.Show($"播放清單「{name}」已成功儲存至手錶！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                await _mainVM.RefreshWatchStatusAsync();
            }
            else
            {
                MessageBox.Show("播放清單儲存失敗，請確認手錶連線狀態。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
