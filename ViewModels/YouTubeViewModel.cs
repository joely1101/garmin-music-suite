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
    private string _dependencyStatus = string.Empty;

    public string DependencyStatus
    {
        get => _dependencyStatus;
        set => SetField(ref _dependencyStatus, value);
    }

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value);
    }

    public string TargetFolder
    {
        get => _targetFolder;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (SetField(ref _targetFolder, value))
            {
                DatabaseService.Instance.SetSetting("youtube_target_folder", value);
            }
        }
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
    public ICommand AddFolderCommand { get; }

    public YouTubeViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
        _targetFolder = DatabaseService.Instance.GetSetting("youtube_target_folder", "音樂");

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
        AddFolderCommand = new RelayCommand(PromptAddFolder);

        DependencyService.Instance.OnStatusChanged += (msg) =>
        {
            App.Current?.Dispatcher?.Invoke(() => DependencyStatus = msg);
        };
    }

    private void PromptAddFolder()
    {
        var dialog = new Views.InputDialog("新增下載分類目錄", "請輸入新目錄名稱 (例如: 跑步音樂、熱門):");
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            var folderName = dialog.InputText.Trim();
            DatabaseService.Instance.CreateFolder(folderName);
            _ = _mainVM.RefreshFoldersAsync();
            TargetFolder = folderName;
        }
    }

    private void StartDownload()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            MessageBox.Show("請輸入有效的 YouTube 影片或音樂網址！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            MessageBox.Show("請先選擇下載目錄！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = TargetFolder.Trim();
        YouTubeDownloadService.Instance.StartDownload(Url.Trim(), target, DownloadPlaylist);
        Url = string.Empty;
    }
}
