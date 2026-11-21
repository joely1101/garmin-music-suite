using System.Collections.ObjectModel;
using System.Threading.Tasks;
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
        set => SetField(ref _targetFolder, value);
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

    public PodcastViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
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
            var target = string.IsNullOrWhiteSpace(TargetFolder) || TargetFolder == "全部歌曲" || TargetFolder == "全部目錄"
                ? "podcast"
                : TargetFolder.Trim();

            PodcastService.Instance.StartDownload(ep, target, SelectedPodcast?.Artist ?? SelectedPodcast?.Title);
        }
    }
}
