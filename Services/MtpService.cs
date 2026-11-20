using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Services;

public class MtpService
{
    private static readonly Lazy<MtpService> _instance = new(() => new MtpService());
    public static MtpService Instance => _instance.Value;

    private readonly string _mtpScriptPath;

    public MtpService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _mtpScriptPath = Path.Combine(baseDir, "bin", "mtp_helper.ps1");
    }

    private async Task<string> RunPsCommandAsync(string arguments)
    {
        if (!File.Exists(_mtpScriptPath)) return string.Empty;

        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{_mtpScriptPath}\" {arguments}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10000);
                return output.Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MTP PS Error: {ex.Message}");
                return string.Empty;
            }
        });
    }

    public async Task<WatchDevice> DetectWatchAsync()
    {
        var device = new WatchDevice();

        // 1. Check custom path from settings
        var customPath = DatabaseService.Instance.GetSetting("watch_custom_path", "");
        if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
        {
            device.IsConnected = true;
            device.DriveOrPath = customPath;
            device.Name = $"自訂路徑 ({Path.GetFileName(customPath.TrimEnd('\\', '/'))})";
            device.IsMtp = false;

            try
            {
                var root = Path.GetPathRoot(customPath);
                if (!string.IsNullOrEmpty(root))
                {
                    var di = new DriveInfo(root);
                    device.TotalBytes = di.TotalSize;
                    device.FreeBytes = di.TotalFreeSpace;
                }
            }
            catch { }
            return device;
        }

        // 2. Scan standard Drive letters
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var root = drive.RootDirectory.FullName;
                var garminXml = Path.Combine(root, "GARMIN", "GARMINDEVICE.XML");
                var musicFolder = Path.Combine(root, "Music");
                var label = drive.VolumeLabel.ToUpperInvariant();

                if (label.Contains("GARMIN") || File.Exists(garminXml) || Directory.Exists(musicFolder))
                {
                    device.IsConnected = true;
                    device.DriveOrPath = Directory.Exists(musicFolder) ? musicFolder : root;
                    device.Name = string.IsNullOrEmpty(drive.VolumeLabel) ? $"磁碟機 ({drive.Name})" : $"{drive.VolumeLabel} ({drive.Name})";
                    device.IsMtp = false;
                    device.TotalBytes = drive.TotalSize;
                    device.FreeBytes = drive.TotalFreeSpace;
                    return device;
                }
            }
            catch { }
        }

        // 3. Scan MTP Device
        var mtpOutput = await RunPsCommandAsync("detect_garmin");
        if (!string.IsNullOrEmpty(mtpOutput))
        {
            try
            {
                using var doc = JsonDocument.Parse(mtpOutput);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var first = root[0];
                    device.IsConnected = true;
                    device.Name = first.GetProperty("name").GetString() ?? "Garmin MTP Watch";
                    device.DriveOrPath = first.GetProperty("path").GetString() ?? "";
                    device.IsMtp = true;

                    // Fetch storage
                    var storageOutput = await RunPsCommandAsync("get_storage");
                    if (!string.IsNullOrEmpty(storageOutput))
                    {
                        try
                        {
                            using var sDoc = JsonDocument.Parse(storageOutput);
                            var sRoot = sDoc.RootElement;
                            if (sRoot.ValueKind == JsonValueKind.Array && sRoot.GetArrayLength() > 0)
                            {
                                var sFirst = sRoot[0];
                                if (sFirst.TryGetProperty("total", out var tProp)) device.TotalBytes = tProp.GetInt64();
                                if (sFirst.TryGetProperty("free", out var fProp)) device.FreeBytes = fProp.GetInt64();
                            }
                        }
                        catch { }
                    }
                    return device;
                }
            }
            catch { }
        }

        device.IsConnected = false;
        return device;
    }

    public async Task<List<WatchTrack>> GetWatchTracksAsync(WatchDevice device)
    {
        var tracks = new List<WatchTrack>();
        if (!device.IsConnected) return tracks;

        if (!device.IsMtp)
        {
            await Task.Run(() =>
            {
                var musicDir = device.DriveOrPath;
                if (!musicDir.EndsWith("Music", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = Path.Combine(musicDir, "Music");
                    if (Directory.Exists(sub)) musicDir = sub;
                }
                if (!Directory.Exists(musicDir)) return;

                foreach (var file in Directory.GetFiles(musicDir, "*.mp3", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(musicDir, file).Replace('\\', '/');
                    var fi = new FileInfo(file);
                    var title = Path.GetFileNameWithoutExtension(file);
                    var artist = "Unknown";
                    try
                    {
                        using var tfile = TagLib.File.Create(file);
                        if (!string.IsNullOrWhiteSpace(tfile.Tag.Title)) title = tfile.Tag.Title;
                        if (!string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer)) artist = tfile.Tag.FirstPerformer;
                    }
                    catch { }

                    tracks.Add(new WatchTrack
                    {
                        Title = title,
                        Artist = artist,
                        RelPath = rel,
                        Size = fi.Length
                    });
                }
            });
        }
        else
        {
            var output = await RunPsCommandAsync("list_files");
            if (!string.IsNullOrEmpty(output))
            {
                try
                {
                    using var doc = JsonDocument.Parse(output);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var name = el.GetProperty("name").GetString() ?? "";
                            var path = el.GetProperty("path").GetString() ?? "";
                            var size = el.TryGetProperty("size", out var sp) ? sp.GetInt64() : 0;

                            if (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                            {
                                tracks.Add(new WatchTrack
                                {
                                    Title = Path.GetFileNameWithoutExtension(name),
                                    Artist = "Garmin MTP",
                                    RelPath = path,
                                    Size = size
                                });
                            }
                        }
                    }
                }
                catch { }
            }
        }
        return tracks;
    }

    public async Task<bool> SyncTrackToWatchAsync(WatchDevice device, Track track)
    {
        if (!device.IsConnected) return false;

        var localFile = Path.Combine(DatabaseService.Instance.MusicDir, track.RelPath.Replace('/', '\\'));
        if (!File.Exists(localFile)) return false;

        if (!device.IsMtp)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var destBase = device.DriveOrPath;
                    if (!destBase.EndsWith("Music", StringComparison.OrdinalIgnoreCase))
                    {
                        var sub = Path.Combine(destBase, "Music");
                        if (!Directory.Exists(sub)) Directory.CreateDirectory(sub);
                        destBase = sub;
                    }

                    var destFile = Path.Combine(destBase, track.RelPath.Replace('/', '\\'));
                    var parent = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                    File.Copy(localFile, destFile, true);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }
        else
        {
            var folder = string.IsNullOrEmpty(track.Folder) ? "音樂" : track.Folder;
            var res = await RunPsCommandAsync($"copy_to_watch -sourceFile \"{localFile}\" -targetSubFolder \"{folder}\"");
            return res.Contains("\"status\": \"ok\"") || res.Contains("copied");
        }
    }

    public async Task<bool> DeleteWatchTrackAsync(WatchDevice device, string relOrFullPath)
    {
        if (!device.IsConnected) return false;

        if (!device.IsMtp)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var target = relOrFullPath;
                    if (!File.Exists(target))
                    {
                        target = Path.Combine(device.DriveOrPath, relOrFullPath.Replace('/', '\\'));
                    }
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                        return true;
                    }
                    return false;
                }
                catch { return false; }
            });
        }
        else
        {
            var res = await RunPsCommandAsync($"delete_file -filePath \"{relOrFullPath}\"");
            return res.Contains("\"status\": \"ok\"");
        }
    }

    public async Task<bool> SaveWatchPlaylistAsync(WatchDevice device, string playlistName, List<string> trackRelPaths)
    {
        if (!device.IsConnected) return false;

        var cleanName = string.Join("_", playlistName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(cleanName)) cleanName = "Playlist";

        var lines = new List<string> { "#EXTM3U", $"#PLAYLIST:{cleanName}" };
        foreach (var p in trackRelPaths)
        {
            var filename = Path.GetFileName(p);
            lines.Add($"#EXTINF:-1,{Path.GetFileNameWithoutExtension(filename)}");
            lines.Add(filename);
        }

        var m3u8Content = string.Join("\r\n", lines);

        if (!device.IsMtp)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var targetDir = device.DriveOrPath;
                    if (!targetDir.EndsWith("Music", StringComparison.OrdinalIgnoreCase))
                    {
                        var sub = Path.Combine(targetDir, "Music");
                        if (Directory.Exists(sub)) targetDir = sub;
                    }
                    var m3u8Path = Path.Combine(targetDir, $"{cleanName}.m3u8");
                    File.WriteAllText(m3u8Path, m3u8Content, System.Text.Encoding.UTF8);
                    return true;
                }
                catch { return false; }
            });
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"{cleanName}.m3u8");
            File.WriteAllText(tempFile, m3u8Content, System.Text.Encoding.UTF8);
            var res = await RunPsCommandAsync($"copy_to_watch -sourceFile \"{tempFile}\"");
            try { File.Delete(tempFile); } catch { }
            return res.Contains("\"status\": \"ok\"") || res.Contains("copied");
        }
    }
}
