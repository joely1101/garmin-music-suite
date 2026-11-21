using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class YouTubeViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string _url = string.Empty;
    private string _targetFolder = "音樂";
    private bool _downloadPlaylist;
    private bool _isDownloading;

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value);
    }

    public string TargetFolder
    {
        get => _targetFolder;
        set => SetField(ref _targetFolder, value);
    }

    public bool DownloadPlaylist
    {
        get => _downloadPlaylist;
        set => SetField(ref _downloadPlaylist, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetField(ref _isDownloading, value);
    }

    public ObservableCollection<DownloadTask> Tasks => YouTubeDownloadService.Instance.Tasks;
    public ObservableCollection<string> Folders => _mainVM.TargetFolders;

    public ICommand StartDownloadCommand { get; }
    public ICommand CancelTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand ClearTasksCommand { get; }

    public YouTubeViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
        StartDownloadCommand = new RelayCommand(StartDownload);
        CancelTaskCommand = new RelayCommand(param =>
        {
            if (param is DownloadTask t) YouTubeDownloadService.Instance.CancelTask(t);
        });
        DeleteTaskCommand = new RelayCommand(param =>
        {
            if (param is DownloadTask t) YouTubeDownloadService.Instance.DeleteTask(t);
        });
        ClearTasksCommand = new RelayCommand(param =>
        {
            YouTubeDownloadService.Instance.ClearTasks();
        });
    }

    private void StartDownload()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            MessageBox.Show("請輸入有效的 YouTube 影片或音樂網址！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = string.IsNullOrWhiteSpace(TargetFolder) || TargetFolder == "全部歌曲" || TargetFolder == "全部目錄"
            ? "音樂"
            : TargetFolder.Trim();

        YouTubeDownloadService.Instance.StartDownload(Url.Trim(), target, DownloadPlaylist);
        Url = string.Empty;
    }
}
