using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using GarminMusicStudio.Models;
using GarminMusicStudio.Services;

namespace GarminMusicStudio.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _mainVM;
    private string _customWatchPath = string.Empty;
    private int _subsonicPort = 48888;
    private bool _isServerRunning;
    private string _localIp = "127.0.0.1";
    private bool _isLoadingDevices;

    public ObservableCollection<DetectedDevice> AvailableDevices { get; } = new();

    public string CustomWatchPath
    {
        get => _customWatchPath;
        set
        {
            if (SetField(ref _customWatchPath, value))
            {
                DatabaseService.Instance.SetSetting("watch_custom_path", value);
                UpdateDeviceSelection();
                _ = _mainVM.RefreshWatchStatusAsync();
            }
        }
    }

    public bool IsLoadingDevices
    {
        get => _isLoadingDevices;
        set => SetField(ref _isLoadingDevices, value);
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
    public ICommand RefreshDevicesCommand { get; }
    public ICommand SelectDeviceCommand { get; }
    public ICommand ToggleServerCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public SettingsViewModel(MainViewModel mainVM)
    {
        _mainVM = mainVM;
        CustomWatchPath = DatabaseService.Instance.GetSetting("watch_custom_path", "");
        SubsonicPort = SubsonicServerService.Instance.Port;
        IsServerRunning = SubsonicServerService.Instance.IsRunning;
        LocalIp = SubsonicServerService.Instance.LocalIpAddress;

        BrowseWatchPathCommand = new RelayCommand(BrowseWatchPath);
        ClearWatchPathCommand = new RelayCommand(ClearWatchPath);
        RefreshDevicesCommand = new AsyncRelayCommand(LoadDevicesAsync);
        SelectDeviceCommand = new RelayCommand(param =>
        {
            if (param is DetectedDevice dev)
            {
                CustomWatchPath = dev.Path;
            }
        });
        ToggleServerCommand = new RelayCommand(ToggleServer);
        OpenLogCommand = new RelayCommand(_ => LogService.Instance.OpenLogFile());
        OpenLogFolderCommand = new RelayCommand(_ => LogService.Instance.OpenLogDirectory());

        _ = LoadDevicesAsync();
    }

    public async Task LoadDevicesAsync()
    {
        IsLoadingDevices = true;
        try
        {
            var list = await MtpService.Instance.GetAllDetectedDevicesAsync(CustomWatchPath);
            AvailableDevices.Clear();
            foreach (var d in list)
            {
                AvailableDevices.Add(d);
            }
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }

    private void UpdateDeviceSelection()
    {
        foreach (var d in AvailableDevices)
        {
            d.IsSelected = !string.IsNullOrEmpty(CustomWatchPath) &&
                           string.Equals(d.Path.TrimEnd('\\', '/'), CustomWatchPath.TrimEnd('\\', '/'), System.StringComparison.OrdinalIgnoreCase);
        }
    }

    private void BrowseWatchPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "請選取手錶音樂資料夾 (Music) 或手錶根目錄 (限本機磁碟/隨身碟)"
        };

        if (dialog.ShowDialog() == true)
        {
            CustomWatchPath = dialog.FolderName;
            _ = LoadDevicesAsync();
        }
    }

    private void ClearWatchPath()
    {
        CustomWatchPath = string.Empty;
        _ = LoadDevicesAsync();
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
