using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class WatchSyncViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string _localFolderFilter = "全部目錄";
    private string _watchFolderFilter = "全部手錶目錄";
    private string _localSearchText = string.Empty;
    private string _watchSearchText = string.Empty;
    private bool _isSelectAllLocal;
    private bool _isSelectAllWatch;
    private bool _isSyncing;
    private string _syncStatusText = string.Empty;

    public AudioPlayerService AudioPlayer => AudioPlayerService.Instance;

    public ObservableCollection<Track> AllLocalTracks { get; } = new();
    public ObservableCollection<WatchTrack> AllWatchTracks { get; } = new();

    public ObservableCollection<LocalFolderGroup> LocalFolderGroups { get; } = new();
    public ObservableCollection<WatchFolderGroup> WatchFolderGroups { get; } = new();

    public ObservableCollection<string> LocalFilterFolders { get; } = new();
    public ObservableCollection<string> WatchFilterFolders { get; } = new();

    public ObservableCollection<WatchPlaylist> WatchPlaylists { get; } = new();

    public string LocalFolderFilter
    {
        get => _localFolderFilter;
        set
        {
            if (SetField(ref _localFolderFilter, value))
            {
                ApplyLocalFilter();
            }
        }
    }

    public string WatchFolderFilter
    {
        get => _watchFolderFilter;
        set
        {
            if (SetField(ref _watchFolderFilter, value))
            {
                ApplyWatchFilter();
            }
        }
    }

    public string LocalSearchText
    {
        get => _localSearchText;
        set
        {
            if (SetField(ref _localSearchText, value))
            {
                ApplyLocalFilter();
            }
        }
    }

    public string WatchSearchText
    {
        get => _watchSearchText;
        set
        {
            if (SetField(ref _watchSearchText, value))
            {
                ApplyWatchFilter();
            }
        }
    }

    public bool IsSelectAllLocal
    {
        get => _isSelectAllLocal;
        set
        {
            if (SetField(ref _isSelectAllLocal, value))
            {
                foreach (var group in LocalFolderGroups)
                {
                    group.IsSelected = value;
                    foreach (var track in group.Tracks)
                    {
                        track.IsSelected = value;
                    }
                }
                UpdateCounts();
            }
        }
    }

    public bool IsSelectAllWatch
    {
        get => _isSelectAllWatch;
        set
        {
            if (SetField(ref _isSelectAllWatch, value))
            {
                foreach (var group in WatchFolderGroups)
                {
                    group.IsSelected = value;
                    foreach (var track in group.Tracks)
                    {
                        track.IsSelected = value;
                    }
                }
                UpdateCounts();
            }
        }
    }

    private int _selectedLocalCount;
    public int SelectedLocalCount
    {
        get => _selectedLocalCount;
        set
        {
            if (SetField(ref _selectedLocalCount, value))
            {
                OnPropertyChanged(nameof(HasSelectedLocal));
                OnPropertyChanged(nameof(SelectedLocalDeleteText));
            }
        }
    }

    public bool HasSelectedLocal => SelectedLocalCount > 0 || LocalFolderGroups.Any(g => g.IsSelected);

    public string SelectedLocalDeleteText
    {
        get
        {
            int tracks = SelectedLocalCount;
            int emptyFolders = LocalFolderGroups.Where(g => g.IsSelected && g.TrackCount == 0).Count();
            if (tracks > 0 && emptyFolders > 0)
                return $"🗑 刪除 ({tracks} 首, {emptyFolders} 個目錄)";
            if (tracks > 0)
                return $"🗑 刪除 ({tracks})";
            if (emptyFolders > 0)
                return $"🗑 刪除 ({emptyFolders} 個目錄)";
            return "🗑 刪除";
        }
    }

    private int _totalFilteredLocalCount;
    public int TotalFilteredLocalCount
    {
        get => _totalFilteredLocalCount;
        set => SetField(ref _totalFilteredLocalCount, value);
    }

    private int _selectedWatchCount;
    public int SelectedWatchCount
    {
        get => _selectedWatchCount;
        set
        {
            if (SetField(ref _selectedWatchCount, value))
            {
                OnPropertyChanged(nameof(HasSelectedWatch));
            }
        }
    }

    public bool HasSelectedWatch => SelectedWatchCount > 0;

    private int _totalFilteredWatchCount;
    public int TotalFilteredWatchCount
    {
        get => _totalFilteredWatchCount;
        set => SetField(ref _totalFilteredWatchCount, value);
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

    public ICommand PlayTrackCommand { get; }
    public ICommand CopySingleTrackCommand { get; }
    public ICommand SyncSelectedCommand { get; }
    public ICommand DeleteTrackCommand { get; }
    public ICommand DeleteSelectedLocalCommand { get; }
    public ICommand DeleteFolderCommand { get; }
    public ICommand RenameTrackCommand { get; }
    public ICommand DeleteWatchTrackCommand { get; }
    public ICommand DeleteSelectedWatchCommand { get; }
    public ICommand ShowPlaylistDetailCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand RefreshLocalCommand { get; }
    public ICommand RefreshWatchCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand OpenMusicDirCommand { get; }

    public WatchSyncViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;

        PlayTrackCommand = new RelayCommand(PlayTrack);
        CopySingleTrackCommand = new AsyncRelayCommand(CopySingleTrackAsync);
        SyncSelectedCommand = new AsyncRelayCommand(SyncSelectedAsync);
        DeleteTrackCommand = new AsyncRelayCommand(DeleteLocalTrackAsync);
        DeleteSelectedLocalCommand = new AsyncRelayCommand(DeleteSelectedLocalAsync);
        DeleteFolderCommand = new AsyncRelayCommand(DeleteFolderAsync);
        RenameTrackCommand = new RelayCommand(RenameLocalTrack);
        DeleteWatchTrackCommand = new AsyncRelayCommand(DeleteWatchTrackItemAsync);
        DeleteSelectedWatchCommand = new AsyncRelayCommand(DeleteSelectedWatchAsync);
        ShowPlaylistDetailCommand = new AsyncRelayCommand(ShowPlaylistDetailAsync);
        CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistFromSelectedAsync);
        RefreshLocalCommand = new AsyncRelayCommand(async () =>
        {
            await DatabaseService.Instance.ScanAndSyncFilesAsync();
            await _mainVM.RefreshFoldersAsync();
            await LoadLocalTracksAsync();
        });
        RefreshWatchCommand = new AsyncRelayCommand(async () =>
        {
            await _mainVM.RefreshWatchStatusAsync();
        });
        AddFolderCommand = new RelayCommand(PromptAddFolder);
        OpenMusicDirCommand = new RelayCommand(OpenMusicDirectory);
    }

    private void OpenMusicDirectory()
    {
        try
        {
            var dir = DatabaseService.Instance.MusicDir;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法開啟音樂資料夾: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void FilterBySidebarFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || folder == "全部歌曲")
        {
            LocalFolderFilter = "全部目錄";
        }
        else
        {
            LocalFolderFilter = folder;
        }
    }

    public async Task LoadLocalTracksAsync()
    {
        var tracks = await DatabaseService.Instance.GetTracksAsync(null, null);
        AllLocalTracks.Clear();
        foreach (var t in tracks)
        {
            t.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Track.IsSelected)) UpdateCounts();
            };
            AllLocalTracks.Add(t);
        }

        // Update local filter folders
        LocalFilterFolders.Clear();
        LocalFilterFolders.Add("全部目錄");
        var folders = DatabaseService.Instance.GetAvailableTargetFolders();
        foreach (var f in folders) LocalFilterFolders.Add(f);

        if (string.IsNullOrEmpty(LocalFolderFilter) || !LocalFilterFolders.Contains(LocalFolderFilter))
        {
            LocalFolderFilter = "全部目錄";
        }

        SyncWatchStatusFlags();
        ApplyLocalFilter();
    }

    public async Task LoadWatchTracksAsync(WatchDevice device)
    {
        var list = await MtpService.Instance.GetWatchTracksAsync(device);
        AllWatchTracks.Clear();
        foreach (var wt in list)
        {
            wt.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WatchTrack.IsSelected)) UpdateCounts();
            };
            AllWatchTracks.Add(wt);
        }

        // Update watch filter folders
        WatchFilterFolders.Clear();
        WatchFilterFolders.Add("全部手錶目錄");
        var folders = AllWatchTracks.Select(t => t.Folder).Where(f => !string.IsNullOrEmpty(f)).Distinct()
            .OrderBy(f => f == "手機撥放清單" ? 0 : 1).ThenBy(f => f);
        foreach (var f in folders) WatchFilterFolders.Add(f);

        if (string.IsNullOrEmpty(WatchFolderFilter) || !WatchFilterFolders.Contains(WatchFolderFilter))
        {
            WatchFolderFilter = "全部手錶目錄";
        }

        SyncWatchStatusFlags();
        ApplyWatchFilter();
    }

    private void SyncWatchStatusFlags()
    {
        foreach (var lt in AllLocalTracks)
        {
            var fileName = Path.GetFileName(lt.RelPath);
            lt.IsSyncedOnWatch = AllWatchTracks.Any(wt => 
                wt.Title.Equals(lt.Title, StringComparison.OrdinalIgnoreCase) ||
                wt.RelPath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ApplyLocalFilter()
    {
        IEnumerable<Track> query = AllLocalTracks;

        if (!string.IsNullOrEmpty(LocalFolderFilter) && LocalFolderFilter != "ALL" && LocalFolderFilter != "全部目錄")
        {
            query = query.Where(t => t.Folder.Equals(LocalFolderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(LocalSearchText))
        {
            var search = LocalSearchText.Trim().ToLowerInvariant();
            query = query.Where(t => t.Title.ToLowerInvariant().Contains(search) || t.Artist.ToLowerInvariant().Contains(search));
        }

        var filteredList = query.ToList();
        TotalFilteredLocalCount = filteredList.Count;

        // Group by Folder
        var groups = filteredList.GroupBy(t => string.IsNullOrEmpty(t.Folder) ? "音樂" : t.Folder)
                                 .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        LocalFolderGroups.Clear();

        if (!string.IsNullOrEmpty(LocalFolderFilter) && LocalFolderFilter != "ALL" && LocalFolderFilter != "全部目錄")
        {
            var groupVm = new LocalFolderGroup(LocalFolderFilter);
            groupVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LocalFolderGroup.IsSelected)) UpdateCounts();
            };
            if (groups.TryGetValue(LocalFolderFilter, out var trackList))
            {
                foreach (var track in trackList) groupVm.Tracks.Add(track);
            }
            LocalFolderGroups.Add(groupVm);
        }
        else
        {
            var allFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in LocalFilterFolders)
            {
                if (f != "全部目錄" && f != "全部歌曲") allFolders.Add(f);
            }
            foreach (var k in groups.Keys) allFolders.Add(k);

            foreach (var f in allFolders.OrderBy(x => x))
            {
                var groupVm = new LocalFolderGroup(f);
                groupVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LocalFolderGroup.IsSelected)) UpdateCounts();
                };
                if (groups.TryGetValue(f, out var trackList))
                {
                    foreach (var track in trackList) groupVm.Tracks.Add(track);
                }
                LocalFolderGroups.Add(groupVm);
            }
        }

        UpdateCounts();
    }

    private void ApplyWatchFilter()
    {
        IEnumerable<WatchTrack> query = AllWatchTracks;

        if (!string.IsNullOrEmpty(WatchFolderFilter) && WatchFolderFilter != "ALL" && WatchFolderFilter != "全部手錶目錄")
        {
            query = query.Where(t => t.Folder.Equals(WatchFolderFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(WatchSearchText))
        {
            var search = WatchSearchText.Trim().ToLowerInvariant();
            query = query.Where(t => t.Title.ToLowerInvariant().Contains(search) || t.Artist.ToLowerInvariant().Contains(search));
        }

        var filteredList = query.ToList();
        TotalFilteredWatchCount = filteredList.Count;

        // Group by Folder: "手機撥放清單" is placed first!
        var groups = filteredList.GroupBy(t => string.IsNullOrEmpty(t.Folder) ? "音樂" : t.Folder)
                                 .OrderBy(g => g.Key == "手機撥放清單" ? 0 : 1)
                                 .ThenBy(g => g.Key);

        WatchFolderGroups.Clear();
        foreach (var g in groups)
        {
            var tracks = g.ToList();
            if (tracks.Count == 0) continue;
            var groupVm = new WatchFolderGroup(g.Key);
            foreach (var track in tracks) groupVm.Tracks.Add(track);
            WatchFolderGroups.Add(groupVm);
        }

        UpdateCounts();
    }

    private void UpdateCounts()
    {
        SelectedLocalCount = LocalFolderGroups.SelectMany(g => g.Tracks).Count(t => t.IsSelected);
        SelectedWatchCount = WatchFolderGroups.SelectMany(g => g.Tracks).Count(t => t.IsSelected);
        OnPropertyChanged(nameof(HasSelectedLocal));
        OnPropertyChanged(nameof(HasSelectedWatch));
        OnPropertyChanged(nameof(SelectedLocalDeleteText));
    }

    private void PlayTrack(object? param)
    {
        if (param is Track track)
        {
            var allTracksInView = LocalFolderGroups.SelectMany(g => g.Tracks).ToList();
            AudioPlayer.PlayTrack(track, allTracksInView);
        }
    }

    private async Task CopySingleTrackAsync(object? param)
    {
        if (param is Track track)
        {
            if (!_mainVM.Watch.IsConnected)
            {
                MessageBox.Show("未偵測到連線中的 Garmin 手錶！請確認手錶已以 USB 連接電腦。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsSyncing = true;
            try
            {
                var progress = new Progress<double>(pct =>
                {
                    SyncStatusText = $"正在複製「{track.Title}」: {pct:F0}%";
                });

                SyncStatusText = $"正在複製「{track.Title}」至手錶...";
                var ok = await MtpService.Instance.SyncTrackToWatchAsync(_mainVM.Watch, track, progress);
                if (ok)
                {
                    SyncStatusText = "複製完成！";
                    await _mainVM.RefreshWatchStatusAsync();
                }
                else
                {
                    MessageBox.Show("複製失敗，請檢查連線與儲存空間。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"複製失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSyncing = false;
                SyncStatusText = string.Empty;
            }
        }
    }

    private async Task SyncSelectedAsync()
    {
        var selected = LocalFolderGroups.SelectMany(g => g.Tracks).Where(t => t.IsSelected).ToList();
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
                var progress = new Progress<double>(pct =>
                {
                    SyncStatusText = $"同步中 ({current}/{total}): {t.Title} ({pct:F0}%)...";
                });

                SyncStatusText = $"同步中 ({current}/{total}): {t.Title}...";
                await MtpService.Instance.SyncTrackToWatchAsync(_mainVM.Watch, t, progress);
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
                await DatabaseService.Instance.ScanAndSyncFilesAsync();
                await _mainVM.RefreshFoldersAsync();
                await LoadLocalTracksAsync();
            }
        }
    }

    private async Task DeleteFolderAsync(object? param)
    {
        if (param is LocalFolderGroup group)
        {
            string prompt = group.TrackCount == 0
                ? $"確定要刪除空目錄「{group.Name}」嗎？\n此動作將一併從電腦硬碟刪除該目錄。"
                : $"確定要刪除目錄「{group.Name}」及其包含的 {group.TrackCount} 首歌曲嗎？\n此動作將一併從電腦硬碟刪除該目錄與歌曲檔案。";

            if (MessageBox.Show(prompt, "確認刪除目錄", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await DatabaseService.Instance.DeleteFolderAsync(group.Name);
                await DatabaseService.Instance.ScanAndSyncFilesAsync();
                await _mainVM.RefreshFoldersAsync();
                await LoadLocalTracksAsync();
            }
        }
    }

    private async Task DeleteSelectedLocalAsync()
    {
        var selectedTracks = LocalFolderGroups.SelectMany(g => g.Tracks).Where(t => t.IsSelected).ToList();
        var selectedFolders = LocalFolderGroups.Where(g => g.IsSelected).ToList();
        if (selectedTracks.Count == 0 && selectedFolders.Count == 0) return;

        string prompt;
        if (selectedTracks.Count == 0 && selectedFolders.Count > 0)
        {
            prompt = $"確定要刪除已勾選的 {selectedFolders.Count} 個目錄嗎？\n此動作將一併從電腦硬碟刪除目錄與檔案。";
        }
        else if (selectedTracks.Count > 0 && selectedFolders.Any(f => f.TrackCount == 0))
        {
            int emptyCount = selectedFolders.Count(f => f.TrackCount == 0);
            prompt = $"確定要從電腦刪除已勾選的 {selectedTracks.Count} 首歌曲與 {emptyCount} 個空目錄嗎？";
        }
        else
        {
            prompt = $"確定要從電腦刪除已勾選的 {selectedTracks.Count} 首歌曲嗎？";
        }

        if (MessageBox.Show(prompt, "確認批次刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            foreach (var t in selectedTracks)
            {
                await DatabaseService.Instance.DeleteTrackAsync(t.Id);
            }
            foreach (var g in selectedFolders)
            {
                await DatabaseService.Instance.DeleteFolderAsync(g.Name);
            }
            await DatabaseService.Instance.ScanAndSyncFilesAsync();
            await _mainVM.RefreshFoldersAsync();
            await LoadLocalTracksAsync();
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

    private async Task DeleteWatchTrackItemAsync(object? param)
    {
        if (param is WatchTrack wt)
        {
            if (MessageBox.Show($"確定要從手錶中刪除歌曲「{wt.Title}」嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                using var watcher = AutoConfirmWatcher.Start();
                var ok = await MtpService.Instance.DeleteWatchTrackAsync(_mainVM.Watch, wt.RelPath);
                if (ok)
                {
                    await _mainVM.RefreshWatchStatusAsync();
                }
                else
                {
                    MessageBox.Show("刪除失敗，請確認手錶連線。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async Task DeleteSelectedWatchAsync()
    {
        var selected = WatchFolderGroups.SelectMany(g => g.Tracks).Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (MessageBox.Show($"確定要從手錶刪除已勾選的 {selected.Count} 首歌曲嗎？", "確認批次刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            using var watcher = AutoConfirmWatcher.Start();
            var paths = selected.Select(wt => wt.RelPath).ToList();
            await MtpService.Instance.DeleteWatchTracksBatchAsync(_mainVM.Watch, paths);
            await _mainVM.RefreshWatchStatusAsync();
        }
    }

    private async Task ShowPlaylistDetailAsync(object? param)
    {
        if (param is WatchTrack track)
        {
            var device = _mainVM.Watch;
            if (device == null)
            {
                MessageBox.Show("請先確認手錶已成功連線。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entries = await MtpService.Instance.GetWatchPlaylistEntriesAsync(device, track);
            var dialog = new Views.PlaylistDetailDialog(track.Title, entries)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }

    private async Task CreatePlaylistFromSelectedAsync()
    {
        var selectedWatch = WatchFolderGroups.SelectMany(g => g.Tracks).Where(t => t.IsSelected).ToList();
        var selectedLocal = LocalFolderGroups.SelectMany(g => g.Tracks).Where(t => t.IsSelected).ToList();

        List<string> relPaths = new();
        string sourceDesc = "";

        if (selectedWatch.Count > 0 && selectedLocal.Count > 0)
        {
            relPaths = selectedWatch.Select(t => t.RelPath)
                       .Concat(selectedLocal.Select(t => t.RelPath))
                       .Distinct()
                       .ToList();
            sourceDesc = $"已勾選的 {relPaths.Count} 首歌曲 (手錶 {selectedWatch.Count} 首 + 電腦 {selectedLocal.Count} 首)";
        }
        else if (selectedWatch.Count > 0)
        {
            relPaths = selectedWatch.Select(t => t.RelPath).ToList();
            sourceDesc = $"手錶已勾選的 {selectedWatch.Count} 首歌曲";
        }
        else if (selectedLocal.Count > 0)
        {
            relPaths = selectedLocal.Select(t => t.RelPath).ToList();
            sourceDesc = $"電腦已勾選的 {selectedLocal.Count} 首歌曲";
        }
        else
        {
            MessageBox.Show("請先勾選欲加入播放清單的歌曲！\n（可從上方「電腦音樂庫」或下方「Garmin 手錶音樂庫」勾選歌曲）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Views.InputDialog("手錶播放清單", $"已選取 {sourceDesc}。\n請輸入播放清單名稱 (例如: 跑步訓練):");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var name = dialog.InputText.Trim();
            using var watcher = AutoConfirmWatcher.Start();
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

    private void PromptAddFolder()
    {
        var dialog = new Views.InputDialog("新增分類目錄", "請輸入新目錄名稱 (例如: 跑步音樂、流行歌):");
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var folderName = dialog.InputText.Trim();
            var targetDir = Path.Combine(DatabaseService.Instance.MusicDir, folderName);
            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    _ = _mainVM.RefreshFoldersAsync();
                    _ = LoadLocalTracksAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"建立目錄失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
