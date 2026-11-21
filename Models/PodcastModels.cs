using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GarminMusicStudio.Models;

public class PodcastSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty;
    public string ArtworkUrl { get; set; } = string.Empty;
    public int TrackCount { get; set; }
}

public class PodcastEpisode : INotifyPropertyChanged
{
    private bool _isDownloading;
    private string _statusText = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string PublishedDate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;

    public string DurationFormatted => string.IsNullOrWhiteSpace(Duration) ? "" : Duration;

    public DateTime? ReleaseDate
    {
        get
        {
            if (DateTime.TryParse(PublishedDate, out var dt)) return dt;
            return null;
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetField(ref _isDownloading, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
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

public class FolderItem : INotifyPropertyChanged
{
    private int _count;
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;

    public int Count
    {
        get => _count;
        set => SetField(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
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
