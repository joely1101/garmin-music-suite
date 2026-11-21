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

    private async Task<string> RunPsCommandAsync(string arguments, int timeoutMs = 30000)
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
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return string.Empty;
                var output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                }
                return output.Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MTP PS Error: {ex.Message}");
                return string.Empty;
            }
        });
    }

    public static long ParseSizeStringToBytes(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
        try
        {
            var s = sizeStr.Trim();
            double multiplier = 1;
            if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1024L * 1024L * 1024L;
                s = s[..^2].Trim();
            }
            else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1024L * 1024L;
                s = s[..^2].Trim();
            }
            else if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1024L;
                s = s[..^2].Trim();
            }
            else if (s.EndsWith("B", StringComparison.OrdinalIgnoreCase))
            {
                s = s[..^1].Trim();
            }

            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                return (long)(num * multiplier);
            }
        }
        catch { }
        return 0;
    }

    public async Task<List<DetectedDevice>> GetAllDetectedDevicesAsync(string currentWatchPath)
    {
        var list = new List<DetectedDevice>();

        // 1. Scan MTP Garmin devices
        var mtpOutput = await RunPsCommandAsync("-Action detect_garmin");
        if (!string.IsNullOrEmpty(mtpOutput))
        {
            try
            {
                using var doc = JsonDocument.Parse(mtpOutput);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var name = el.GetProperty("name").GetString() ?? "Garmin 手錶";
                        var path = el.GetProperty("path").GetString() ?? "";
                        var totalStr = el.TryGetProperty("total_str", out var tp) ? tp.GetString() ?? "" : "";
                        var freeStr = el.TryGetProperty("free_str", out var fp) ? fp.GetString() ?? "" : "";
                        var usedPct = el.TryGetProperty("used_pct", out var up) ? up.GetString() ?? "" : "";

                        string storage = "";
                        if (!string.IsNullOrEmpty(totalStr) || !string.IsNullOrEmpty(freeStr))
                        {
                            storage = $"容量: {totalStr} / 剩餘: {freeStr} ({usedPct})";
                        }

                        bool isSelected = !string.IsNullOrEmpty(currentWatchPath) &&
                                          string.Equals(path.TrimEnd('\\', '/'), currentWatchPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

                        list.Add(new DetectedDevice
                        {
                            Name = name,
                            Path = path,
                            DeviceType = "Garmin MTP 手錶 (推薦)",
                            StorageInfo = storage,
                            IsMtp = true,
                            IsSelected = isSelected
                        });
                    }
                }
            }
            catch { }
        }

        // 2. Scan physical Drive Letters
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var root = drive.RootDirectory.FullName;
                var garminXml = Path.Combine(root, "GARMIN", "GARMINDEVICE.XML");
                var musicFolder = Path.Combine(root, "Music");
                var label = drive.VolumeLabel;

                bool isGarminDrive = label.Contains("GARMIN", StringComparison.OrdinalIgnoreCase) || File.Exists(garminXml);
                bool isRemovable = drive.DriveType == DriveType.Removable;

                if (isGarminDrive || isRemovable)
                {
                    var targetPath = Directory.Exists(musicFolder) ? musicFolder : root;
                    double freeGb = drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    string devType = isGarminDrive ? "Garmin 隨身碟/手錶" : "USB 隨身碟";

                    bool isSelected = !string.IsNullOrEmpty(currentWatchPath) &&
                                      (string.Equals(targetPath.TrimEnd('\\', '/'), currentWatchPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(root.TrimEnd('\\', '/'), currentWatchPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));

                    list.Add(new DetectedDevice
                    {
                        Name = string.IsNullOrEmpty(label) ? $"磁碟機 ({drive.Name.TrimEnd('\\')})" : $"{label} ({drive.Name.TrimEnd('\\')})",
                        Path = targetPath,
                        DeviceType = devType,
                        StorageInfo = $"容量: {totalGb:F1} GB / 剩餘: {freeGb:F2} GB",
                        IsMtp = false,
                        IsSelected = isSelected
                    });
                }
            }
        }
        catch { }

        return list;
    }

    public async Task<WatchDevice> DetectWatchAsync()
    {
        var device = new WatchDevice();

        // 1. Check custom path from settings
        var customPath = DatabaseService.Instance.GetSetting("watch_custom_path", "").Trim();
        if (!string.IsNullOrEmpty(customPath))
        {
            if (customPath.StartsWith("mtp:\\", StringComparison.OrdinalIgnoreCase) || customPath.StartsWith("mtp:/", StringComparison.OrdinalIgnoreCase))
            {
                var clean = customPath.Substring(5).Replace('/', '\\').Trim('\\');
                var parts = clean.Split('\\');
                var devName = parts[0];
                var subPath = parts.Length > 1 ? string.Join("\\", parts.Skip(1)) : "Music";

                device.IsConnected = true;
                device.IsMtp = true;
                device.Name = devName;
                device.DriveOrPath = customPath;

                var storageJson = await RunPsCommandAsync($"-Action get_storage -DeviceName \"{devName}\" -SubPath \"{subPath}\"");
                if (!string.IsNullOrEmpty(storageJson))
                {
                    try
                    {
                        using var sDoc = JsonDocument.Parse(storageJson);
                        var sRoot = sDoc.RootElement;
                        if (sRoot.TryGetProperty("total_str", out var tp))
                            device.TotalBytes = ParseSizeStringToBytes(tp.GetString() ?? "");
                        if (sRoot.TryGetProperty("free_str", out var fp))
                            device.FreeBytes = ParseSizeStringToBytes(fp.GetString() ?? "");
                    }
                    catch { }
                }
                return device;
            }
            else if (Directory.Exists(customPath))
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
        }

        // 2. Scan MTP Device FIRST! (Priority: Real Garmin MTP Watch)
        var mtpOutput = await RunPsCommandAsync("-Action detect_garmin");
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

                    if (first.TryGetProperty("total_str", out var tp))
                        device.TotalBytes = ParseSizeStringToBytes(tp.GetString() ?? "");
                    if (first.TryGetProperty("free_str", out var fp))
                        device.FreeBytes = ParseSizeStringToBytes(fp.GetString() ?? "");

                    return device;
                }
            }
            catch { }
        }

        // 3. Scan standard Drive letters (Strict match: ONLY if label contains GARMIN or GARMINDEVICE.XML exists)
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var root = drive.RootDirectory.FullName;
                var garminXml = Path.Combine(root, "GARMIN", "GARMINDEVICE.XML");
                var musicFolder = Path.Combine(root, "Music");
                var label = drive.VolumeLabel.ToUpperInvariant();

                // Strict check: DO NOT match solely because "Music" directory exists!
                if (label.Contains("GARMIN") || File.Exists(garminXml))
                {
                    device.IsConnected = true;
                    device.DriveOrPath = Directory.Exists(musicFolder) ? musicFolder : root;
                    device.Name = string.IsNullOrEmpty(drive.VolumeLabel) ? $"Garmin ({drive.Name})" : $"{drive.VolumeLabel} ({drive.Name})";
                    device.IsMtp = false;
                    device.TotalBytes = drive.TotalSize;
                    device.FreeBytes = drive.TotalFreeSpace;
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

                foreach (var file in Directory.GetFiles(musicDir, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".mp3" && ext != ".m4a" && ext != ".aac" && ext != ".flac") continue;

                    var rel = Path.GetRelativePath(musicDir, file).Replace('\\', '/');
                    var fi = new FileInfo(file);
                    var title = Path.GetFileNameWithoutExtension(file);
                    var artist = "Garmin Watch";

                    var folder = Path.GetDirectoryName(rel)?.Replace('\\', '/');
                    if (string.IsNullOrEmpty(folder) || folder == ".") folder = "音樂";

                    tracks.Add(new WatchTrack
                    {
                        Title = title,
                        Artist = artist,
                        RelPath = rel,
                        Folder = folder,
                        Size = fi.Length
                    });
                }
            });
        }
        else
        {
            string devName = device.Name;
            string subPath = "Internal Storage\\Music";
            var targetStr = device.DriveOrPath;
            if (targetStr.StartsWith("mtp:\\", StringComparison.OrdinalIgnoreCase) || targetStr.StartsWith("mtp:/", StringComparison.OrdinalIgnoreCase))
            {
                var clean = targetStr.Substring(5).Replace('/', '\\').Trim('\\');
                var parts = clean.Split('\\', 2);
                devName = parts[0];
                subPath = parts.Length > 1 ? parts[1] : "Internal Storage\\Music";
            }

            var output = await RunPsCommandAsync($"-Action list_files -DeviceName \"{devName}\" -SubPath \"{subPath}\"", timeoutMs: 40000);
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
                            var relPath = el.TryGetProperty("rel_path", out var rp) ? rp.GetString() ?? name : name;
                            
                            // Check folder, then sub_folder, then relative path parent
                            var folder = "";
                            if (el.TryGetProperty("folder", out var fp) && !string.IsNullOrWhiteSpace(fp.GetString()))
                            {
                                folder = fp.GetString()!;
                            }
                            else if (el.TryGetProperty("sub_folder", out var sfp) && !string.IsNullOrWhiteSpace(sfp.GetString()))
                            {
                                folder = sfp.GetString()!;
                            }
                            else if (!string.IsNullOrEmpty(relPath) && (relPath.Contains('\\') || relPath.Contains('/')))
                            {
                                var parts = relPath.Replace('/', '\\').Split('\\');
                                if (parts.Length > 1) folder = parts[0];
                            }

                            if (string.IsNullOrWhiteSpace(folder) || folder == ".")
                            {
                                folder = "手錶音樂";
                            }

                            var size = el.TryGetProperty("size", out var sp) ? sp.GetInt64() : 0;
                            var sizeStr = el.TryGetProperty("size_str", out var ssp) ? ssp.GetString() ?? "" : "";

                            if (size == 0 && !string.IsNullOrEmpty(sizeStr))
                            {
                                size = ParseSizeStringToBytes(sizeStr);
                            }

                            // Always use clean file name without extension for Title
                            var title = Path.GetFileNameWithoutExtension(name);
                            var artist = "Garmin 手錶";

                            tracks.Add(new WatchTrack
                            {
                                Title = title,
                                Artist = artist,
                                RelPath = path,
                                Folder = folder,
                                Size = size
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to parse MTP files list: {ex.Message}");
                }
            }
        }
        return tracks;
    }

    public async Task<bool> SyncTrackToWatchAsync(WatchDevice device, Track track, IProgress<double>? progress = null)
    {
        if (!device.IsConnected) return false;

        var localFile = Path.Combine(DatabaseService.Instance.MusicDir, track.RelPath.Replace('/', '\\'));
        if (!File.Exists(localFile)) return false;

        if (!device.IsMtp)
        {
            return await Task.Run(async () =>
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

                    const int bufferSize = 256 * 1024;
                    var fileLength = new FileInfo(localFile).Length;

                    using (var sourceStream = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
                    using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
                    {
                        var buffer = new byte[bufferSize];
                        long totalBytes = 0;
                        int read;

                        while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await destStream.WriteAsync(buffer, 0, read);
                            totalBytes += read;
                            if (fileLength > 0 && progress != null)
                            {
                                progress.Report((double)totalBytes / fileLength * 100.0);
                            }
                        }
                        await destStream.FlushAsync();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Copy error: {ex.Message}");
                    return false;
                }
            });
        }
        else
        {
            // MTP device copy
            string devName = device.Name;
            string subPath = "Internal Storage\\Music";
            var targetStr = device.DriveOrPath;
            if (targetStr.StartsWith("mtp:\\", StringComparison.OrdinalIgnoreCase) || targetStr.StartsWith("mtp:/", StringComparison.OrdinalIgnoreCase))
            {
                var clean = targetStr.Substring(5).Replace('/', '\\').Trim('\\');
                var parts = clean.Split('\\', 2);
                devName = parts[0];
                subPath = parts.Length > 1 ? parts[1] : "Internal Storage\\Music";
            }

            var folder = (string.IsNullOrEmpty(track.Folder) || track.Folder == "全部歌曲") ? "" : track.Folder;
            var targetSub = string.IsNullOrEmpty(folder) ? subPath : Path.Combine(subPath, folder);

            var res = await RunPsCommandAsync($"-Action copy_file -DeviceName \"{devName}\" -SubPath \"{targetSub}\" -SourceFile \"{localFile}\"", timeoutMs: 180000);
            return res.Contains("\"success\": true") || res.Contains("\"success\":true");
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
            string devName = device.Name;
            string subPath = "Internal Storage\\Music";
            string fileName = Path.GetFileName(relOrFullPath);

            if (relOrFullPath.StartsWith("mtp:\\", StringComparison.OrdinalIgnoreCase) || relOrFullPath.StartsWith("mtp:/", StringComparison.OrdinalIgnoreCase))
            {
                var clean = relOrFullPath.Substring(5).Replace('/', '\\').Trim('\\');
                var parts = clean.Split('\\');
                if (parts.Length >= 2)
                {
                    devName = parts[0];
                    fileName = parts[^1];
                    subPath = string.Join('\\', parts.Skip(1).Take(parts.Length - 2));
                }
            }

            var res = await RunPsCommandAsync($"-Action delete_file -DeviceName \"{devName}\" -SubPath \"{subPath}\" -FileName \"{fileName}\"", timeoutMs: 20000);
            return res.Contains("\"success\": true") || res.Contains("\"success\":true");
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
                        if (Directory.Exists(sub)) destBase = sub;
                    }

                    var playlistFile = Path.Combine(destBase, $"{cleanName}.m3u8");
                    File.WriteAllLines(playlistFile, lines, System.Text.Encoding.UTF8);
                    return true;
                }
                catch { return false; }
            });
        }
        else
        {
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"{cleanName}.m3u8");
                await File.WriteAllLinesAsync(tempFile, lines, System.Text.Encoding.UTF8);

                string devName = device.Name;
                string subPath = "Internal Storage\\Music";
                var targetStr = device.DriveOrPath;
                if (targetStr.StartsWith("mtp:\\", StringComparison.OrdinalIgnoreCase) || targetStr.StartsWith("mtp:/", StringComparison.OrdinalIgnoreCase))
                {
                    var clean = targetStr.Substring(5).Replace('/', '\\').Trim('\\');
                    var parts = clean.Split('\\', 2);
                    devName = parts[0];
                    subPath = parts.Length > 1 ? parts[1] : "Internal Storage\\Music";
                }

                var res = await RunPsCommandAsync($"-Action copy_file -DeviceName \"{devName}\" -SubPath \"{subPath}\" -SourceFile \"{tempFile}\"", timeoutMs: 20000);
                try { File.Delete(tempFile); } catch { }

                return res.Contains("\"success\": true") || res.Contains("\"success\":true");
            }
            catch { return false; }
        }
    }
}
