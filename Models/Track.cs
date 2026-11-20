using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GarminMusicStudio.Models;

public class Track : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isSyncedOnWatch;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public int Duration { get; set; }
    public int Bitrate { get; set; } = 192;
    public long Size { get; set; }
    public string RelPath { get; set; } = string.Empty;
    public string Folder { get; set; } = "音樂";
    public bool SyncEnabled { get; set; } = true;
    public string User { get; set; } = "admin";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsSyncedOnWatch
    {
        get => _isSyncedOnWatch;
        set => SetField(ref _isSyncedOnWatch, value);
    }

    public string DurationFormatted
    {
        get
        {
            var m = Duration / 60;
            var s = Duration % 60;
            return $"{m}:{s:D2}";
        }
    }

    public string SizeFormatted
    {
        get
        {
            if (Size < 1024 * 1024)
                return $"{Size / 1024.0:F1} KB";
            return $"{Size / (1024.0 * 1024.0):F1} MB";
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
