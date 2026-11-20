using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GarminMusicStudio.Models;
using GarminMusicStudio.ViewModels;

namespace GarminMusicStudio.Services;

public class AudioPlayerService : ViewModelBase
{
    private static readonly Lazy<AudioPlayerService> _instance = new(() => new AudioPlayerService());
    public static AudioPlayerService Instance => _instance.Value;

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer;
    private bool _isUpdatingFromTimer;
    private Track? _currentTrack;
    private bool _isPlaying;
    private double _currentPositionSeconds;
    private double _durationSeconds = 100;
    private double _playbackRate = 1.0;
    private List<Track> _playlist = new();
    private int _currentIndex = -1;

    public Track? CurrentTrack
    {
        get => _currentTrack;
        set
        {
            if (SetField(ref _currentTrack, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(CurrentTitle));
                OnPropertyChanged(nameof(CurrentArtist));
            }
        }
    }

    public string CurrentTitle => CurrentTrack?.Title ?? "未播放歌曲";
    public string CurrentArtist => string.IsNullOrWhiteSpace(CurrentTrack?.Artist) ? "Unknown Artist" : CurrentTrack.Artist;

    public bool IsVisible => CurrentTrack != null;

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetField(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";

    public double CurrentPositionSeconds
    {
        get => _currentPositionSeconds;
        set
        {
            if (SetField(ref _currentPositionSeconds, value))
            {
                if (!_isUpdatingFromTimer && Math.Abs(_mediaPlayer.Position.TotalSeconds - value) > 1.0)
                {
                    try { _mediaPlayer.Position = TimeSpan.FromSeconds(value); } catch { }
                }
                OnPropertyChanged(nameof(CurrentPositionFormatted));
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            if (SetField(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(DurationFormatted));
            }
        }
    }

    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            if (SetField(ref _playbackRate, value))
            {
                _mediaPlayer.SpeedRatio = value;
                OnPropertyChanged(nameof(PlaybackRateFormatted));
            }
        }
    }

    public string PlaybackRateFormatted => $"{PlaybackRate:0.#}x";

    public string CurrentPositionFormatted => FormatTime(CurrentPositionSeconds);
    public string DurationFormatted => FormatTime(DurationSeconds);

    public ICommand TogglePlayPauseCommand { get; }
    public ICommand PlayNextCommand { get; }
    public ICommand PlayPrevCommand { get; }
    public ICommand Forward10Command { get; }
    public ICommand CycleSpeedCommand { get; }

    private static string FormatTime(double sec)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    public AudioPlayerService()
    {
        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause);
        PlayNextCommand = new RelayCommand(PlayNext);
        PlayPrevCommand = new RelayCommand(PlayPrev);
        Forward10Command = new RelayCommand(Forward10);
        CycleSpeedCommand = new RelayCommand(CycleSpeed);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (s, e) =>
        {
            if (IsPlaying && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                _isUpdatingFromTimer = true;
                try
                {
                    CurrentPositionSeconds = _mediaPlayer.Position.TotalSeconds;
                }
                finally
                {
                    _isUpdatingFromTimer = false;
                }
                var total = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (total > 0 && Math.Abs(DurationSeconds - total) > 0.5)
                {
                    DurationSeconds = total;
                }
            }
        };

        _mediaPlayer.MediaOpened += (s, e) =>
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                DurationSeconds = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            }
        };

        _mediaPlayer.MediaEnded += (s, e) =>
        {
            PlayNext();
        };
    }

    public void PlayTrack(Track track, IEnumerable<Track>? currentList = null)
    {
        if (currentList != null)
        {
            _playlist = currentList.ToList();
            _currentIndex = _playlist.IndexOf(track);
        }

        if (CurrentTrack == track)
        {
            TogglePlayPause();
            return;
        }

        CurrentTrack = track;
        var fullPath = Path.Combine(DatabaseService.Instance.MusicDir, track.RelPath.Replace('/', '\\'));
        if (!File.Exists(fullPath)) return;

        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Open(new Uri(fullPath));
            _mediaPlayer.SpeedRatio = PlaybackRate;
            _mediaPlayer.Play();
            IsPlaying = true;
            CurrentPositionSeconds = 0;
            if (track.Duration > 0)
            {
                DurationSeconds = track.Duration;
            }
            _timer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Playback error: {ex.Message}");
        }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            _mediaPlayer.Pause();
            IsPlaying = false;
            _timer.Stop();
        }
        else
        {
            if (CurrentTrack == null && _playlist.Count > 0)
            {
                PlayTrack(_playlist[0]);
                return;
            }
            _mediaPlayer.Play();
            IsPlaying = true;
            _timer.Start();
        }
    }

    public void PlayNext()
    {
        if (_playlist.Count == 0) return;
        var nextIdx = (_currentIndex + 1) % _playlist.Count;
        _currentIndex = nextIdx;
        PlayTrack(_playlist[nextIdx]);
    }

    public void PlayPrev()
    {
        if (_playlist.Count == 0) return;
        var prevIdx = (_currentIndex - 1 + _playlist.Count) % _playlist.Count;
        _currentIndex = prevIdx;
        PlayTrack(_playlist[prevIdx]);
    }

    public void Forward10()
    {
        var target = Math.Min(DurationSeconds, CurrentPositionSeconds + 10);
        Seek(target);
    }

    public void CycleSpeed()
    {
        if (PlaybackRate < 1.1) PlaybackRate = 1.2;
        else if (PlaybackRate < 1.4) PlaybackRate = 1.5;
        else PlaybackRate = 1.0;
    }

    public void Seek(double seconds)
    {
        _mediaPlayer.Position = TimeSpan.FromSeconds(seconds);
        CurrentPositionSeconds = seconds;
    }
}
