using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GarminMusicStudio.Models;

public class DownloadTask : INotifyPropertyChanged
{
    private string _status = "queued";
    private string _progress = "準備中...";
    private string _title = string.Empty;
    private string? _error;

    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string TargetFolder { get; set; } = "音樂";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string? Error
    {
        get => _error;
        set => SetField(ref _error, value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

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
