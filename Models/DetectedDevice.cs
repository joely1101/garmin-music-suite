namespace GarminMusicStudio.Models;

public class DetectedDevice
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string StorageInfo { get; set; } = string.Empty;
    public bool IsMtp { get; set; }
    public bool IsSelected { get; set; }
}
