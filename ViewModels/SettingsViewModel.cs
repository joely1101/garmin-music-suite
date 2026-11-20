using System.Windows.Input;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string _customWatchPath = string.Empty;
    private int _subsonicPort = 28888;
    private bool _isServerRunning;
    private string _localIp = "127.0.0.1";

    public string CustomWatchPath
    {
        get => _customWatchPath;
        set
        {
            if (SetField(ref _customWatchPath, value))
            {
                DatabaseService.Instance.SetSetting("watch_custom_path", value);
                _ = _mainVM.RefreshWatchStatusAsync();
            }
        }
    }

    public int SubsonicPort
    {
        get => _subsonicPort;
        set => SetField(ref _subsonicPort, value);
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        set => SetField(ref _isServerRunning, value);
    }

    public string LocalIp
    {
        get => _localIp;
        set => SetField(ref _localIp, value);
    }

    public string SubsonicUrl => $"http://{LocalIp}:{SubsonicPort}/rest";

    public ICommand BrowseWatchPathCommand { get; }
    public ICommand ClearWatchPathCommand { get; }
    public ICommand ToggleServerCommand { get; }

    public SettingsViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
        CustomWatchPath = DatabaseService.Instance.GetSetting("watch_custom_path", "");
        SubsonicPort = SubsonicServerService.Instance.Port;
        IsServerRunning = SubsonicServerService.Instance.IsRunning;
        LocalIp = SubsonicServerService.Instance.LocalIpAddress;

        BrowseWatchPathCommand = new RelayCommand(BrowseWatchPath);
        ClearWatchPathCommand = new RelayCommand(ClearWatchPath);
        ToggleServerCommand = new RelayCommand(ToggleServer);
    }

    private void BrowseWatchPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "請選取手錶音樂資料夾 (Music) 或手錶根目錄"
        };

        if (dialog.ShowDialog() == true)
        {
            CustomWatchPath = dialog.FolderName;
        }
    }

    private void ClearWatchPath()
    {
        CustomWatchPath = string.Empty;
    }

    private void ToggleServer()
    {
        if (IsServerRunning)
        {
            SubsonicServerService.Instance.Stop();
            IsServerRunning = false;
        }
        else
        {
            SubsonicServerService.Instance.Start(SubsonicPort);
            IsServerRunning = SubsonicServerService.Instance.IsRunning;
            LocalIp = SubsonicServerService.Instance.LocalIpAddress;
            OnPropertyChanged(nameof(SubsonicUrl));
        }
    }
}
