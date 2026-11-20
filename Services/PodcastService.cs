using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Services;

public class PodcastService
{
    private static readonly Lazy<PodcastService> _instance = new(() => new PodcastService());
    public static PodcastService Instance => _instance.Value;

    private readonly HttpClient _http;

    public PodcastService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) GarminMusicStudio/1.0");
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
            System.Diagnostics.Debug.WriteLine($"Podcast search error: {ex.Message}");
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
            var items = doc.Descendants("item");

            int count = 0;
            foreach (var item in items)
            {
                if (count++ >= 30) break; // Limit to 30 latest episodes

                var title = item.Element("title")?.Value ?? "Podcast Episode";
                var pubDate = item.Element("pubDate")?.Value ?? "";
                var desc = item.Element("description")?.Value ?? "";

                // Find audio enclosure
                var enc = item.Element("enclosure");
                string? audioUrl = enc?.Attribute("url")?.Value;
                if (string.IsNullOrEmpty(audioUrl))
                {
                    // search by media:content
                    var media = item.Elements().FirstOrDefault(e => e.Name.LocalName == "content");
                    audioUrl = media?.Attribute("url")?.Value;
                }

                if (!string.IsNullOrEmpty(audioUrl))
                {
                    episodes.Add(new PodcastEpisode
                    {
                        Title = title,
                        AudioUrl = audioUrl,
                        PublishedDate = pubDate,
                        Description = desc
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Podcast feed parse error: {ex.Message}");
        }
        return episodes;
    }

    public async Task<bool> DownloadEpisodeAsync(PodcastEpisode episode, string targetFolder = "音樂")
    {
        episode.IsDownloading = true;
        episode.StatusText = "下載中...";

        try
        {
            var cleanFolder = string.Join("_", targetFolder.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrEmpty(cleanFolder)) cleanFolder = "音樂";

            var targetDir = Path.Combine(DatabaseService.Instance.MusicDir, cleanFolder);
            Directory.CreateDirectory(targetDir);

            var cleanTitle = string.Join("_", episode.Title.Split(Path.GetInvalidFileNameChars())).Trim();
            if (cleanTitle.Length > 50) cleanTitle = cleanTitle[..50];
            var targetFile = Path.Combine(targetDir, $"{cleanTitle}.mp3");

            using (var resp = await _http.GetAsync(episode.AudioUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
                await stream.CopyToAsync(fs);
            }

            episode.StatusText = "已完成";
            _ = DatabaseService.Instance.ScanAndSyncFilesAsync();
            return true;
        }
        catch (Exception ex)
        {
            episode.StatusText = $"下載失敗: {ex.Message}";
            return false;
        }
        finally
        {
            episode.IsDownloading = false;
        }
    }
}
