using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using GarminMusicStudio.ViewModels;

namespace GarminMusicStudio.Models;

public class LocalFolderGroup : ViewModelBase
{
    private string _name = string.Empty;
    private bool _isExpanded = false;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandIcon));
                OnPropertyChanged(nameof(ExpandTooltip));
            }
        }
    }

    public string ExpandIcon => IsExpanded ? "▼" : "▶";
    public string ExpandTooltip => IsExpanded ? "收合" : "展開";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                foreach (var t in Tracks) t.IsSelected = value;
            }
        }
    }

    public ObservableCollection<Track> Tracks { get; } = new();

    public bool CanDelete => !string.Equals(Name, "音樂", System.StringComparison.OrdinalIgnoreCase) && 
                             !string.Equals(Name, "podcast", System.StringComparison.OrdinalIgnoreCase);

    public int TrackCount
    {
        get => Tracks.Count;
        set { }
    }

    public ICommand ToggleExpandCommand { get; }

    public LocalFolderGroup(string name)
    {
        Name = name;
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        Tracks.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(TrackCount));
        };
    }
}

public class WatchFolderGroup : ViewModelBase
{
    private string _name = string.Empty;
    private bool _isExpanded = false;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandIcon));
                OnPropertyChanged(nameof(ExpandTooltip));
            }
        }
    }

    public string ExpandIcon => IsExpanded ? "▼" : "▶";
    public string ExpandTooltip => IsExpanded ? "收合" : "展開";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                foreach (var t in Tracks) t.IsSelected = value;
            }
        }
    }

    public ObservableCollection<WatchTrack> Tracks { get; } = new();

    public bool CanDelete => !string.Equals(Name, "音樂", System.StringComparison.OrdinalIgnoreCase) && 
                             !string.Equals(Name, "podcast", System.StringComparison.OrdinalIgnoreCase);

    public int TrackCount
    {
        get => Tracks.Count;
        set { }
    }

    public ICommand ToggleExpandCommand { get; }

    public WatchFolderGroup(string name)
    {
        Name = name;
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        Tracks.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(TrackCount));
        };
    }
}
