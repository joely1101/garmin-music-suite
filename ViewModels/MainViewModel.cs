using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class MainViewModel : ViewModelBase
{
    private int _selectedTabIndex;
    private WatchDevice _watch = new();
    private FolderItem? _selectedFolder;
    private bool _isRefreshingWatch;

    public WatchSyncViewModel WatchSyncVM { get; }
    public YouTubeViewModel YouTubeVM { get; }
    public PodcastViewModel PodcastVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public AudioPlayerService AudioPlayer => AudioPlayerService.Instance;

    public ObservableCollection<FolderItem> Folders { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetField(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsWatchSyncTab));
                OnPropertyChanged(nameof(IsYouTubeTab));
                OnPropertyChanged(nameof(IsPodcastTab));
                OnPropertyChanged(nameof(IsSettingsTab));
            }
        }
    }

    public bool IsWatchSyncTab => SelectedTabIndex == 0;
    public bool IsYouTubeTab => SelectedTabIndex == 1;
    public bool IsPodcastTab => SelectedTabIndex == 2;
    public bool IsSettingsTab => SelectedTabIndex == 3;

    public WatchDevice Watch
    {
        get => _watch;
        set => SetField(ref _watch, value);
    }

    public FolderItem? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetField(ref _selectedFolder, value))
            {
                WatchSyncVM.FilterBySidebarFolder(value?.Name);
            }
        }
    }

    public bool IsRefreshingWatch
    {
        get => _isRefreshingWatch;
        set => SetField(ref _isRefreshingWatch, value);
    }

    public ICommand SelectTabCommand { get; }
    public ICommand RefreshWatchCommand { get; }
    public ICommand AddFolderCommand { get; }

    public MainViewModel()
    {
        WatchSyncVM = new WatchSyncViewModel(this);
        YouTubeVM = new YouTubeViewModel(this);
        PodcastVM = new PodcastViewModel(this);
        SettingsVM = new SettingsViewModel(this);

        SelectTabCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out var idx))
            {
                SelectedTabIndex = idx;
            }
        });

        RefreshWatchCommand = new AsyncRelayCommand(RefreshWatchStatusAsync);
        AddFolderCommand = new RelayCommand(PromptAddFolder);

        // Initial setup
        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await DatabaseService.Instance.ScanAndSyncFilesAsync();
        await RefreshFoldersAsync();
        await WatchSyncVM.LoadLocalTracksAsync();
        await RefreshWatchStatusAsync();
        
        // Start Subsonic server
        SubsonicServerService.Instance.Start(48888);
    }

    public async Task RefreshFoldersAsync()
    {
        var list = await DatabaseService.Instance.GetFoldersAsync();
        Folders.Clear();
        foreach (var f in list) Folders.Add(f);

        if (SelectedFolder == null && Folders.Count > 0)
        {
            SelectedFolder = Folders[0];
        }
    }

    public async Task RefreshWatchStatusAsync()
    {
        IsRefreshingWatch = true;
        try
        {
            Watch = await MtpService.Instance.DetectWatchAsync();
            await WatchSyncVM.LoadWatchTracksAsync(Watch);
        }
        finally
        {
            IsRefreshingWatch = false;
        }
    }

    private void PromptAddFolder()
    {
        // Simple input dialog
        var dialog = new Views.InputDialog("新增電腦分類目錄", "請輸入新分類目錄名稱 (例如: 180步頻):");
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            DatabaseService.Instance.CreateFolder(dialog.InputText.Trim());
            _ = RefreshFoldersAsync();
        }
    }
}
