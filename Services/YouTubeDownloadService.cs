using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Services;

public class YouTubeDownloadService
{
    private static readonly Lazy<YouTubeDownloadService> _instance = new(() => new YouTubeDownloadService());
    public static YouTubeDownloadService Instance => _instance.Value;

    public ObservableCollection<DownloadTask> Tasks { get; } = new();

    private readonly string _ytDlpPath;
    private readonly string _ffmpegDir;

    public YouTubeDownloadService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _ytDlpPath = Path.Combine(baseDir, "bin", "yt-dlp.exe");
        _ffmpegDir = Path.Combine(baseDir, "bin");
    }

    public DownloadTask StartDownload(string url, string targetFolder = "音樂", bool downloadPlaylist = false, string? customTitle = null)
    {
        var task = new DownloadTask
        {
            Url = url,
            TargetFolder = string.IsNullOrWhiteSpace(targetFolder) ? "音樂" : targetFolder.Trim(),
            Title = string.IsNullOrWhiteSpace(customTitle) ? url : customTitle,
            Status = "processing",
            Progress = "解析媒體資訊中..."
        };

        App.Current.Dispatcher.Invoke(() => Tasks.Insert(0, task));

        _ = Task.Run(() => RunDownloadTask(task, downloadPlaylist));
        return task;
    }

    public void CancelTask(DownloadTask task)
    {
        try
        {
            if (task.RunningProcess != null && !task.RunningProcess.HasExited)
            {
                task.RunningProcess.Kill(true);
            }
        }
        catch { }

        task.Status = "cancelled";
        task.Progress = "已取消下載";
    }

    public void DeleteTask(DownloadTask task)
    {
        CancelTask(task);
        App.Current.Dispatcher.Invoke(() => Tasks.Remove(task));
    }

    public void ClearTasks()
    {
        foreach (var task in Tasks.ToList())
        {
            CancelTask(task);
        }
        App.Current.Dispatcher.Invoke(() => Tasks.Clear());
    }

    private void RunDownloadTask(DownloadTask task, bool downloadPlaylist)
    {
        try
        {
            var cleanFolder = string.Join("_", task.TargetFolder.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrEmpty(cleanFolder)) cleanFolder = "音樂";

            var targetDir = Path.Combine(DatabaseService.Instance.MusicDir, cleanFolder);
            Directory.CreateDirectory(targetDir);

            var cleanUrl = task.Url.Trim();
            if (cleanUrl.Contains("music.youtube.com", StringComparison.OrdinalIgnoreCase))
            {
                cleanUrl = cleanUrl.Replace("music.youtube.com", "www.youtube.com");
            }

            if (!downloadPlaylist && cleanUrl.Contains("watch?v=", StringComparison.OrdinalIgnoreCase))
            {
                cleanUrl = Regex.Replace(cleanUrl, @"[?&]list=[^&]+", "");
            }

            if (!File.Exists(_ytDlpPath))
            {
                throw new FileNotFoundException("找不到 yt-dlp.exe，請確認 bin 目錄完整性。");
            }

            var playlistFlag = downloadPlaylist ? "--yes-playlist" : "--no-playlist";
            var outTmpl = Path.Combine(targetDir, "%(title)s.%(ext)s");

            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = $"-x --audio-format mp3 --audio-quality 192K --postprocessor-args \"ExtractAudio+ffmpeg:-ar 44100 -ac 2 -id3v2_version 3\" --ffmpeg-location \"{_ffmpegDir}\" {playlistFlag} -o \"{outTmpl}\" \"{cleanUrl}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };
            task.RunningProcess = proc;
            string lastOutput = string.Empty;

            proc.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data) || task.Status == "cancelled") return;
                lastOutput = e.Data;

                var line = e.Data;
                if (line.Contains("[download]") && line.Contains("%"))
                {
                    var match = Regex.Match(line, @"\[download\]\s+(\d+\.?\d*%)");
                    if (match.Success)
                    {
                        var pct = match.Groups[1].Value;
                        task.Status = "downloading";
                        task.Progress = $"下載中 ({pct})...";
                    }
                }
                else if (line.Contains("[ExtractAudio]") || line.Contains("[FixupM4a]"))
                {
                    task.Status = "processing";
                    task.Progress = "轉碼 MP3 中 (192k)...";
                }
                else if (line.Contains("[youtube]") || line.Contains("[info]"))
                {
                    task.Status = "processing";
                    task.Progress = "擷取音訊資料中...";
                }
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && task.Status != "cancelled")
                {
                    lastOutput = e.Data;
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            if (task.Status == "cancelled")
            {
                return;
            }

            if (proc.ExitCode == 0)
            {
                task.Status = "completed";
                task.Progress = "下載與轉碼完成";
                _ = DatabaseService.Instance.ScanAndSyncFilesAsync();
            }
            else
            {
                task.Status = "failed";
                task.Error = lastOutput;
                task.Progress = $"下載失敗: {lastOutput}";
            }
        }
        catch (Exception ex)
        {
            if (task.Status != "cancelled")
            {
                task.Status = "failed";
                task.Error = ex.Message;
                task.Progress = $"錯誤: {ex.Message}";
            }
        }
        finally
        {
            task.RunningProcess = null;
        }
    }
}
