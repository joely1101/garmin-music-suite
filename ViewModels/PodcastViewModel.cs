using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class PodcastViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string _searchQuery = string.Empty;
    private string _targetFolder = "podcast";
    private bool _isSearching;
    private bool _isLoadingEpisodes;
    private PodcastSearchResult? _selectedPodcast;

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetField(ref _searchQuery, value);
    }

    public string TargetFolder
    {
        get => _targetFolder;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (SetField(ref _targetFolder, value))
            {
                DatabaseService.Instance.SetSetting("podcast_target_folder", value);
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => SetField(ref _isSearching, value);
    }

    public bool IsLoadingEpisodes
    {
        get => _isLoadingEpisodes;
        set => SetField(ref _isLoadingEpisodes, value);
    }

    public PodcastSearchResult? SelectedPodcast
    {
        get => _selectedPodcast;
        set
        {
            if (SetField(ref _selectedPodcast, value) && value != null)
            {
                _ = LoadEpisodesAsync(value.FeedUrl);
            }
        }
    }

    public ObservableCollection<PodcastSearchResult> SearchResults { get; } = new();
    public ObservableCollection<PodcastEpisode> Episodes { get; } = new();
    public ObservableCollection<DownloadTask> Tasks => PodcastService.Instance.Tasks;
    public ObservableCollection<string> Folders => _mainVM.TargetFolders;

    public ICommand SearchCommand { get; }
    public ICommand DownloadEpisodeCommand { get; }
    public ICommand CancelTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand ClearTasksCommand { get; }
    public ICommand AddFolderCommand { get; }

    public PodcastViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
        _targetFolder = DatabaseService.Instance.GetSetting("podcast_target_folder", "podcast");

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        DownloadEpisodeCommand = new RelayCommand(DownloadEpisodeItem);
        CancelTaskCommand = new RelayCommand(param =>
        {
            if (param is DownloadTask t) PodcastService.Instance.CancelTask(t);
        });
        DeleteTaskCommand = new RelayCommand(param =>
        {
            if (param is DownloadTask t) PodcastService.Instance.DeleteTask(t);
        });
        ClearTasksCommand = new RelayCommand(param =>
        {
            PodcastService.Instance.ClearTasks();
        });
        AddFolderCommand = new RelayCommand(PromptAddFolder);
    }

    private void PromptAddFolder()
    {
        var dialog = new Views.InputDialog("新增下載分類目錄", "請輸入新目錄名稱 (例如: 科技、慢跑):");
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var folderName = dialog.InputText.Trim();
            DatabaseService.Instance.CreateFolder(folderName);
            _ = _mainVM.RefreshFoldersAsync();
            TargetFolder = folderName;
        }
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        try
        {
            var list = await PodcastService.Instance.SearchPodcastsAsync(SearchQuery);
            SearchResults.Clear();
            foreach (var p in list) SearchResults.Add(p);

            Episodes.Clear();
            SelectedPodcast = null;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task LoadEpisodesAsync(string feedUrl)
    {
        IsLoadingEpisodes = true;
        Episodes.Clear();
        try
        {
            var list = await PodcastService.Instance.GetEpisodesAsync(feedUrl);
            foreach (var ep in list) Episodes.Add(ep);
        }
        finally
        {
            IsLoadingEpisodes = false;
        }
    }

    private void DownloadEpisodeItem(object? param)
    {
        if (param is PodcastEpisode ep)
        {
            if (string.IsNullOrWhiteSpace(TargetFolder))
            {
                MessageBox.Show("請先選擇下載目錄！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = TargetFolder.Trim();
            PodcastService.Instance.StartDownload(ep, target, SelectedPodcast?.Artist ?? SelectedPodcast?.Title);
        }
    }
}
