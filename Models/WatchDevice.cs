using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GarminMusicStudio.Models;

public class WatchDevice : INotifyPropertyChanged
{
    private string _name = "Garmin Watch";
    private string _driveOrPath = string.Empty;
    private bool _isMtp;
    private long _totalBytes;
    private long _freeBytes;
    private bool _isConnected;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string DriveOrPath
    {
        get => _driveOrPath;
        set => SetField(ref _driveOrPath, value);
    }

    public bool IsMtp
    {
        get => _isMtp;
        set => SetField(ref _isMtp, value);
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (SetField(ref _totalBytes, value))
            {
                OnPropertyChanged(nameof(UsedBytes));
                OnPropertyChanged(nameof(UsedPercentage));
                OnPropertyChanged(nameof(StorageDisplayText));
            }
        }
    }

    public long FreeBytes
    {
        get => _freeBytes;
        set
        {
            if (SetField(ref _freeBytes, value))
            {
                OnPropertyChanged(nameof(UsedBytes));
                OnPropertyChanged(nameof(UsedPercentage));
                OnPropertyChanged(nameof(StorageDisplayText));
            }
        }
    }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercentage
    {
        get
        {
            if (TotalBytes <= 0) return 0;
            return (double)UsedBytes / TotalBytes * 100.0;
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public string StorageDisplayText
    {
        get
        {
            if (!IsConnected) return "手錶未連接";
            double freeGb = FreeBytes / (1024.0 * 1024.0 * 1024.0);
            double totalGb = TotalBytes / (1024.0 * 1024.0 * 1024.0);
            string type = IsMtp ? "MTP" : DriveOrPath;
            return $"{type} (剩餘 {freeGb:F2} GB / {totalGb:F1} GB)";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class WatchTrack
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = "Unknown";
    public string RelPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeFormatted
    {
        get
        {
            if (Size < 1024 * 1024)
                return $"{Size / 1024.0:F1} KB";
            return $"{Size / (1024.0 * 1024.0):F1} MB";
        }
    }
}

public class WatchPlaylist
{
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public System.Collections.Generic.List<string> TrackRelPaths { get; set; } = new();
}
