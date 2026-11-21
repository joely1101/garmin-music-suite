using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Services;

public class PodcastService
{
    private static readonly Lazy<PodcastService> _instance = new(() => new PodcastService());
    public static PodcastService Instance => _instance.Value;

    public ObservableCollection<DownloadTask> Tasks { get; } = new();

    private readonly HttpClient _http;

    public PodcastService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 50,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromHours(1)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public async Task<List<PodcastSearchResult>> SearchPodcastsAsync(string query)
    {
        var list = new List<PodcastSearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return list;

        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query.Trim())}&media=podcast&entity=podcast&limit=15";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    var feedUrl = el.TryGetProperty("feedUrl", out var f) ? f.GetString() : null;
                    if (string.IsNullOrEmpty(feedUrl)) continue;

                    list.Add(new PodcastSearchResult
                    {
                        Id = el.TryGetProperty("collectionId", out var cid) ? cid.ToString() : Guid.NewGuid().ToString(),
                        Title = el.TryGetProperty("collectionName", out var cn) ? cn.GetString() ?? "" : "",
                        Artist = el.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "",
                        FeedUrl = feedUrl,
                        ArtworkUrl = el.TryGetProperty("artworkUrl600", out var a600) ? a600.GetString() ?? "" :
                                     (el.TryGetProperty("artworkUrl100", out var a100) ? a100.GetString() ?? "" : ""),
                        TrackCount = el.TryGetProperty("trackCount", out var tc) ? tc.GetInt32() : 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Podcast search error: {ex.Message}");
        }
        return list;
    }

    public async Task<List<PodcastEpisode>> GetEpisodesAsync(string feedUrl)
    {
        var episodes = new List<PodcastEpisode>();
        if (string.IsNullOrWhiteSpace(feedUrl)) return episodes;

        try
        {
            var xml = await _http.GetStringAsync(feedUrl);
            var doc = XDocument.Parse(xml);
            var items = doc.Descendants().Where(e => e.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase));

            int count = 0;
            foreach (var item in items)
            {
                if (count++ >= 40) break;

                var title = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))?.Value ?? "Podcast Episode";
                var pubDate = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pubDate", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                var desc = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                var durationStr = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("duration", StringComparison.OrdinalIgnoreCase))?.Value ?? "";

                // Find audio enclosure or media:content
                var enc = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase));
                string? audioUrl = enc?.Attribute("url")?.Value;
                if (string.IsNullOrEmpty(audioUrl))
                {
                    var media = item.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase));
                    audioUrl = media?.Attribute("url")?.Value;
                }

                if (!string.IsNullOrEmpty(audioUrl))
                {
                    episodes.Add(new PodcastEpisode
                    {
                        Title = title.Trim(),
                        AudioUrl = audioUrl.Trim(),
                        PublishedDate = pubDate,
                        Description = Regex.Replace(desc, "<.*?>", string.Empty).Trim(),
                        Duration = durationStr
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Podcast feed parse error: {ex.Message}");
        }
        return episodes;
    }

    public DownloadTask StartDownload(PodcastEpisode episode, string targetFolder = "podcast", string? podcastArtist = null)
    {
        var task = new DownloadTask
        {
            Url = episode.AudioUrl,
            TargetFolder = string.IsNullOrWhiteSpace(targetFolder) ? "podcast" : targetFolder.Trim(),
            Title = episode.Title,
            Status = "downloading",
            Progress = "連線音訊伺服器中..."
        };

        var cts = new CancellationTokenSource();
        task.Cts = cts;

        App.Current.Dispatcher.Invoke(() => Tasks.Insert(0, task));

        _ = Task.Run(() => DownloadEpisodeWorkerAsync(task, episode, podcastArtist, cts.Token));
        return task;
    }

    public void CancelTask(DownloadTask task)
    {
        try
        {
            task.Cts?.Cancel();
        }
        catch { }

        task.Status = "cancelled";
        task.Progress = "已取消下載";

        // Remove incomplete file if exists
        if (!string.IsNullOrEmpty(task.OutputFilePath) && File.Exists(task.OutputFilePath))
        {
            try { File.Delete(task.OutputFilePath); } catch { }
        }
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

    private string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        name = Regex.Replace(name, @"[\r\n\t]+", " ").Trim();
        name = name.Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Podcast_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
        if (name.Length > 70)
        {
            name = name.Substring(0, 70).Trim('.', ' ');
        }
        return name;
    }

    private async Task DownloadEpisodeWorkerAsync(DownloadTask task, PodcastEpisode episode, string? podcastArtist, CancellationToken ct)
    {
        string? targetFile = null;
        try
        {
            var cleanFolder = SanitizeFileName(task.TargetFolder);
            if (string.IsNullOrEmpty(cleanFolder)) cleanFolder = "podcast";

            var targetDir = Path.Combine(DatabaseService.Instance.MusicDir, cleanFolder);
            Directory.CreateDirectory(targetDir);

            var cleanTitle = SanitizeFileName(episode.Title);
            targetFile = Path.Combine(targetDir, $"{cleanTitle}.mp3");
            task.OutputFilePath = targetFile;

            // Handle potential file name duplicate
            int counter = 1;
            while (File.Exists(targetFile))
            {
                targetFile = Path.Combine(targetDir, $"{cleanTitle}_{counter}.mp3");
                task.OutputFilePath = targetFile;
                counter++;
            }

            episode.IsDownloading = true;
            episode.StatusText = "下載中...";

            using var request = new HttpRequestMessage(HttpMethod.Get, episode.AudioUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloadedBytes = 0;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                int bytesRead;
                var sw = Stopwatch.StartNew();

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    downloadedBytes += bytesRead;

                    if (sw.ElapsedMilliseconds > 300)
                    {
                        sw.Restart();
                        if (totalBytes > 0)
                        {
                            double pct = (double)downloadedBytes / totalBytes * 100.0;
                            double curMb = downloadedBytes / (1024.0 * 1024.0);
                            double totMb = totalBytes / (1024.0 * 1024.0);
                            task.Progress = $"下載中 ({pct:F1}%) - {curMb:F1} MB / {totMb:F1} MB";
                            episode.StatusText = $"下載中 ({pct:F0}%)";
                        }
                        else
                        {
                            double curMb = downloadedBytes / (1024.0 * 1024.0);
                            task.Progress = $"下載中 - {curMb:F1} MB";
                            episode.StatusText = $"下載中 {curMb:F1}MB";
                        }
                    }
                }
            }

            // Write ID3 tags for Garmin Watch compatibility
            try
            {
                using var tagFile = TagLib.File.Create(targetFile);
                tagFile.Tag.Title = episode.Title;
                if (!string.IsNullOrWhiteSpace(podcastArtist))
                {
                    tagFile.Tag.Performers = new[] { podcastArtist };
                    tagFile.Tag.Album = podcastArtist;
                }
                tagFile.Save();
            }
            catch (Exception tagEx)
            {
                Debug.WriteLine($"Tagging error: {tagEx.Message}");
            }

            task.Status = "completed";
            task.Progress = "下載完成";
            episode.StatusText = "已完成";

            await DatabaseService.Instance.ScanAndSyncFilesAsync();
        }
        catch (OperationCanceledException)
        {
            task.Status = "cancelled";
            task.Progress = "已取消下載";
            episode.StatusText = "已取消";
            if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
            {
                try { File.Delete(targetFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            if (task.Status != "cancelled")
            {
                task.Status = "failed";
                task.Error = ex.Message;
                task.Progress = $"下載失敗: {ex.Message}";
                episode.StatusText = "下載失敗";
            }
            if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
            {
                try { File.Delete(targetFile); } catch { }
            }
        }
        finally
        {
            episode.IsDownloading = false;
        }
    }
}
