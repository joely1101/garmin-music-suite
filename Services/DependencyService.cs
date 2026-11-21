using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace GarminMusicStudio.Services;

public class DependencyService
{
    private static readonly Lazy<DependencyService> _instance = new(() => new DependencyService());
    public static DependencyService Instance => _instance.Value;

    public string BinDir { get; }
    public string YtDlpPath { get; }
    public string FfmpegPath { get; }
    public string MtpHelperPath { get; }

    public bool IsYtDlpReady => File.Exists(YtDlpPath);
    public bool IsFfmpegReady => File.Exists(FfmpegPath);
    public bool IsAllReady => IsYtDlpReady && IsFfmpegReady && File.Exists(MtpHelperPath);

    public event Action<string>? OnStatusChanged;
    public bool IsDownloadingDependencies { get; private set; }

    public DependencyService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        BinDir = Path.Combine(baseDir, "bin");
        Directory.CreateDirectory(BinDir);

        YtDlpPath = Path.Combine(BinDir, "yt-dlp.exe");
        FfmpegPath = Path.Combine(BinDir, "ffmpeg.exe");
        MtpHelperPath = Path.Combine(BinDir, "mtp_helper.ps1");
    }

    public async Task EnsureDependenciesAsync()
    {
        if (IsDownloadingDependencies) return;

        // 1. Try extracting embedded binaries directly from inside the .exe
        ExtractEmbeddedBinary("mtp_helper.ps1", MtpHelperPath);
        ExtractEmbeddedBinary("yt-dlp.exe", YtDlpPath);
        ExtractEmbeddedBinary("ffmpeg.exe", FfmpegPath);

        if (IsAllReady)
        {
            OnStatusChanged?.Invoke("必要組件已就緒 (yt-dlp.exe, ffmpeg.exe, mtp_helper.ps1)");
            return;
        }

        IsDownloadingDependencies = true;
        try
        {
            // 2. If yt-dlp.exe was not embedded, download it
            if (!IsYtDlpReady)
            {
                OnStatusChanged?.Invoke("正在自動補充 yt-dlp.exe 下載組件...");
                var okYt = await DownloadFileWithRetryAsync(
                    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                    YtDlpPath);

                if (!okYt)
                {
                    await DownloadFileWithRetryAsync(
                        "https://github.com/joely1101/garmin-music-suite/releases/latest/download/yt-dlp.exe",
                        YtDlpPath);
                }
            }

            // 3. If ffmpeg.exe was not embedded, check system PATH or download it
            if (!IsFfmpegReady)
            {
                var sysFfmpeg = FindInSystemPath("ffmpeg.exe");
                if (!string.IsNullOrEmpty(sysFfmpeg) && File.Exists(sysFfmpeg))
                {
                    try { File.Copy(sysFfmpeg, FfmpegPath, true); } catch { }
                }

                if (!IsFfmpegReady)
                {
                    OnStatusChanged?.Invoke("正在自動補充 ffmpeg.exe 轉檔組件...");
                    var okFfmpeg = await DownloadFileWithRetryAsync(
                        "https://github.com/joely1101/garmin-music-suite/releases/latest/download/ffmpeg.exe",
                        FfmpegPath);

                    if (!okFfmpeg || !IsFfmpegReady)
                    {
                        await DownloadAndExtractFfmpegAsync();
                    }
                }
            }

            if (IsAllReady)
            {
                OnStatusChanged?.Invoke("必要組件已全部就緒 (yt-dlp.exe, ffmpeg.exe, mtp_helper.ps1)");
            }
            else
            {
                OnStatusChanged?.Invoke("部分組件下載未完成，於需要時將自動重試");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureDependenciesAsync error: {ex.Message}");
            OnStatusChanged?.Invoke($"組件補充發生錯誤: {ex.Message}");
        }
        finally
        {
            IsDownloadingDependencies = false;
        }
    }

    private static bool ExtractEmbeddedBinary(string partialName, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 1024) return true;

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var resNames = asm.GetManifestResourceNames();
            var resName = System.Linq.Enumerable.FirstOrDefault(resNames, r => 
                r.EndsWith(partialName, StringComparison.OrdinalIgnoreCase) || 
                r.IndexOf(partialName.Replace('-', '_'), StringComparison.OrdinalIgnoreCase) >= 0);

            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null && stream.Length > 0)
                {
                    var temp = targetPath + ".tmp";
                    using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(fs);
                    }
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(temp, targetPath);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static string? FindInSystemPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    private async Task<bool> DownloadFileWithRetryAsync(string url, string destPath)
    {
        var tempPath = destPath + ".tmp";
        for (int i = 0; i < 3; i++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) GarminMusicStudio");
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    await using (var stream = await response.Content.ReadAsStreamAsync())
                    await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fs);
                    }

                    if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 1024)
                    {
                        if (File.Exists(destPath)) File.Delete(destPath);
                        File.Move(tempPath, destPath);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Download attempt {i + 1} failed for {url}: {ex.Message}");
                await Task.Delay(1000);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        return false;
    }

    private async Task<bool> DownloadAndExtractFfmpegAsync()
    {
        var zipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        var tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg_essentials.zip");
        try
        {
            var downloaded = await DownloadFileWithRetryAsync(zipUrl, tempZip);
            if (!downloaded || !File.Exists(tempZip)) return false;

            using var archive = ZipFile.OpenRead(tempZip);
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ExtractToFile(FfmpegPath, true);
                    break;
                }
            }
            return File.Exists(FfmpegPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DownloadAndExtractFfmpegAsync error: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }
}
