using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using GarminMusicStudio.Models;

namespace GarminMusicStudio.Services;

public class DatabaseService
{
    private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
    public static DatabaseService Instance => _instance.Value;

    public string AppDir { get; }
    public string DataDir { get; }
    public string MusicDir { get; }
    public string DbPath { get; }

    private readonly string _connectionString;

    public DatabaseService()
    {
        AppDir = AppDomain.CurrentDomain.BaseDirectory;
        DataDir = Path.Combine(AppDir, "data");
        MusicDir = Path.Combine(AppDir, "music");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(MusicDir);

        DbPath = Path.Combine(DataDir, "music.db");
        _connectionString = $"Data Source={DbPath}";

        InitDb();
    }

    private void InitDb()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS tracks (
                id TEXT PRIMARY KEY,
                title TEXT,
                artist TEXT,
                album TEXT,
                duration INTEGER DEFAULT 0,
                bitrate INTEGER DEFAULT 320,
                size INTEGER DEFAULT 0,
                rel_path TEXT UNIQUE,
                folder TEXT DEFAULT '',
                sync_enabled INTEGER DEFAULT 1,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                user TEXT DEFAULT 'admin'
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task ScanAndSyncFilesAsync()
    {
        await Task.Run(() =>
        {
            var existingMap = new Dictionary<string, string>(); // rel_path -> id
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, rel_path FROM tracks";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existingMap[reader.GetString(1)] = reader.GetString(0);
                }
            }

            var foundRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var files = Directory.GetFiles(MusicDir, "*.mp3", SearchOption.AllDirectories);
            using var trans = conn.BeginTransaction();

            foreach (var file in files)
            {
                var relPath = Path.GetRelativePath(MusicDir, file).Replace('\\', '/');
                foundRelPaths.Add(relPath);

                var parentDir = Path.GetDirectoryName(file);
                string folder = "音樂";
                if (!string.IsNullOrEmpty(parentDir) && !string.Equals(parentDir, MusicDir, StringComparison.OrdinalIgnoreCase))
                {
                    folder = Path.GetRelativePath(MusicDir, parentDir).Replace('\\', '/');
                }

                if (!existingMap.ContainsKey(relPath))
                {
                    var id = Guid.NewGuid().ToString();
                    var title = Path.GetFileNameWithoutExtension(file);
                    var artist = "Unknown Artist";
                    var album = "Unknown Album";
                    int duration = 0;
                    int bitrate = 192;
                    long size = 0;

                    try
                    {
                        var fi = new FileInfo(file);
                        size = fi.Length;
                        using var tagFile = TagLib.File.Create(file);
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                            title = tagFile.Tag.Title;
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer))
                            artist = tagFile.Tag.FirstPerformer;
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                            album = tagFile.Tag.Album;
                        duration = (int)tagFile.Properties.Duration.TotalSeconds;
                        bitrate = tagFile.Properties.AudioBitrate > 0 ? tagFile.Properties.AudioBitrate : 192;
                    }
                    catch
                    {
                        // fallback to filename
                    }

                    using var insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = trans;
                    insertCmd.CommandText = @"
                        INSERT INTO tracks (id, title, artist, album, duration, bitrate, size, rel_path, folder, sync_enabled, user)
                        VALUES (@id, @title, @artist, @album, @duration, @bitrate, @size, @rel_path, @folder, 1, 'admin');
                    ";
                    insertCmd.Parameters.AddWithValue("@id", id);
                    insertCmd.Parameters.AddWithValue("@title", title);
                    insertCmd.Parameters.AddWithValue("@artist", artist);
                    insertCmd.Parameters.AddWithValue("@album", album);
                    insertCmd.Parameters.AddWithValue("@duration", duration);
                    insertCmd.Parameters.AddWithValue("@bitrate", bitrate);
                    insertCmd.Parameters.AddWithValue("@size", size);
                    insertCmd.Parameters.AddWithValue("@rel_path", relPath);
                    insertCmd.Parameters.AddWithValue("@folder", folder);
                    insertCmd.ExecuteNonQuery();
                }
            }

            // Cleanup deleted files
            foreach (var kvp in existingMap)
            {
                if (!foundRelPaths.Contains(kvp.Key))
                {
                    using var delCmd = conn.CreateCommand();
                    delCmd.Transaction = trans;
                    delCmd.CommandText = "DELETE FROM tracks WHERE id = @id";
                    delCmd.Parameters.AddWithValue("@id", kvp.Value);
                    delCmd.ExecuteNonQuery();
                }
            }

            trans.Commit();
        });
    }

    public async Task<List<Track>> GetTracksAsync(string? folder = null, string? search = null)
    {
        return await Task.Run(() =>
        {
            var list = new List<Track>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            var sql = "SELECT id, title, artist, album, duration, bitrate, size, rel_path, folder, sync_enabled, user FROM tracks WHERE 1=1";
            
            if (!string.IsNullOrEmpty(folder))
            {
                sql += " AND folder = @folder";
                cmd.Parameters.AddWithValue("@folder", folder);
            }
            if (!string.IsNullOrEmpty(search))
            {
                sql += " AND (title LIKE @search OR artist LIKE @search OR album LIKE @search)";
                cmd.Parameters.AddWithValue("@search", $"%{search}%");
            }
            sql += " ORDER BY title COLLATE NOCASE ASC";

            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Track
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Artist = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                    Album = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                    Duration = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    Bitrate = reader.IsDBNull(5) ? 192 : reader.GetInt32(5),
                    Size = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    RelPath = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    Folder = reader.IsDBNull(8) ? "音樂" : reader.GetString(8),
                    SyncEnabled = !reader.IsDBNull(9) && reader.GetInt32(9) == 1,
                    User = reader.IsDBNull(10) ? "admin" : reader.GetString(10),
                });
            }
            return list;
        });
    }

    public async Task<List<FolderItem>> GetFoldersAsync()
    {
        return await Task.Run(() =>
        {
            var folderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan physical directories on disk in MusicDir
            try
            {
                if (Directory.Exists(MusicDir))
                {
                    foreach (var dir in Directory.GetDirectories(MusicDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (!string.IsNullOrWhiteSpace(dirName))
                        {
                            folderMap[dirName] = 0;
                        }
                    }
                }
            }
            catch { }

            // Ensure default folders
            if (!folderMap.ContainsKey("音樂")) folderMap["音樂"] = 0;
            if (!folderMap.ContainsKey("podcast")) folderMap["podcast"] = 0;

            // 2. Query tracks count from SQLite database
            int total = 0;
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT folder, COUNT(*) FROM tracks GROUP BY folder";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var f = reader.GetString(0);
                var c = reader.GetInt32(1);
                total += c;
                folderMap[f] = c;
            }

            var folders = folderMap.Select(kvp => new FolderItem { Name = kvp.Key, Count = kvp.Value })
                                   .OrderBy(f => f.Name)
                                   .ToList();

            // Insert "全部歌曲" at the top
            folders.Insert(0, new FolderItem { Name = "全部歌曲", Count = total });
            return folders;
        });
    }

    public List<string> GetAvailableTargetFolders()
    {
        var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        list.Add("音樂");
        list.Add("podcast");

        try
        {
            if (Directory.Exists(MusicDir))
            {
                foreach (var dir in Directory.GetDirectories(MusicDir))
                {
                    var dirName = Path.GetFileName(dir);
                    if (!string.IsNullOrWhiteSpace(dirName))
                    {
                        list.Add(dirName);
                    }
                }
            }
        }
        catch { }

        list.Remove("全部歌曲");
        list.Remove("全部目錄");

        return list.OrderBy(x => x).ToList();
    }

    public void CreateFolder(string folderName)
    {
        var clean = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(clean)) return;
        var dir = Path.Combine(MusicDir, clean);
        Directory.CreateDirectory(dir);
    }

    public async Task DeleteFolderAsync(string folderName)
    {
        await Task.Run(() =>
        {
            // Stop audio player in case any file in this folder is currently open/locked
            try
            {
                AudioPlayerService.Instance.Stop();
            }
            catch { }

            var clean = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrEmpty(clean)) return;

            // Prevent deleting system default folders
            if (string.Equals(clean, "音樂", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, "podcast", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LogService.Instance.LogInfo($"DeleteFolderAsync: Deleting folder '{folderName}' (clean: '{clean}')");

            // 1. Delete all tracks in this folder from database
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM tracks WHERE folder = @folder OR folder = @clean";
                cmd.Parameters.AddWithValue("@folder", folderName);
                cmd.Parameters.AddWithValue("@clean", clean);
                var deletedCount = cmd.ExecuteNonQuery();
                LogService.Instance.LogInfo($"Deleted {deletedCount} tracks from DB for folder '{folderName}'");
            }

            // 2. Clear saved folder setting if it was this folder
            var lastYt = GetSetting("last_yt_target_folder", "");
            if (string.Equals(lastYt, folderName, StringComparison.OrdinalIgnoreCase))
            {
                SetSetting("last_yt_target_folder", "音樂");
            }
            var lastPod = GetSetting("last_podcast_target_folder", "");
            if (string.Equals(lastPod, folderName, StringComparison.OrdinalIgnoreCase))
            {
                SetSetting("last_podcast_target_folder", "podcast");
            }

            // 3. Delete the physical directory on disk safely
            var dir = Path.Combine(MusicDir, clean);
            if (!Directory.Exists(dir))
            {
                // Try case-insensitive matching in case actual disk directory casing differs
                var actualDir = Directory.GetDirectories(MusicDir)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), clean, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(actualDir))
                {
                    dir = actualDir;
                }
            }

            if (Directory.Exists(dir))
            {
                try
                {
                    // Clean readonly attributes on all files and subdirs to prevent UnauthorizedAccessException
                    var di = new DirectoryInfo(dir);
                    foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            file.Attributes = FileAttributes.Normal;
                            file.Delete();
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.LogWarn($"DeleteFolderAsync: failed to delete file {file.FullName}: {ex.Message}");
                        }
                    }

                    foreach (var subDir in di.GetDirectories("*", SearchOption.AllDirectories).OrderByDescending(d => d.FullName.Length))
                    {
                        try
                        {
                            subDir.Delete(true);
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.LogWarn($"DeleteFolderAsync: failed to delete subDir {subDir.FullName}: {ex.Message}");
                        }
                    }

                    Directory.Delete(dir, true);
                    LogService.Instance.LogInfo($"DeleteFolderAsync: Successfully removed directory '{dir}' from disk.");
                }
                catch (Exception ex)
                {
                    LogService.Instance.LogError($"DeleteFolderAsync: Failed to delete directory '{dir}': {ex.Message}", ex);
                    throw;
                }
            }
            else
            {
                LogService.Instance.LogWarn($"DeleteFolderAsync: Directory '{dir}' does not exist on disk.");
            }
        });
    }

    public async Task DeleteTrackAsync(string trackId)
    {
        await Task.Run(() =>
        {
            // Stop audio player in case this track is playing
            try
            {
                AudioPlayerService.Instance.Stop();
            }
            catch { }

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string? relPath = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT rel_path FROM tracks WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", trackId);
                relPath = cmd.ExecuteScalar() as string;
            }

            if (!string.IsNullOrEmpty(relPath))
            {
                var fullPath = Path.Combine(MusicDir, relPath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.SetAttributes(fullPath, FileAttributes.Normal);
                        File.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.LogWarn($"Failed to delete file {fullPath}: {ex.Message}");
                    }
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM tracks WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", trackId);
                cmd.ExecuteNonQuery();
            }
        });
    }

    public async Task RenameTrackAsync(string trackId, string newTitle, string newArtist)
    {
        await Task.Run(() =>
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string? relPath = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT rel_path FROM tracks WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", trackId);
                relPath = cmd.ExecuteScalar() as string;
            }

            if (!string.IsNullOrEmpty(relPath))
            {
                var fullPath = Path.Combine(MusicDir, relPath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        using var tagFile = TagLib.File.Create(fullPath);
                        tagFile.Tag.Title = newTitle;
                        tagFile.Tag.Performers = new[] { newArtist };
                        tagFile.Save();
                    }
                    catch { }
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE tracks SET title = @title, artist = @artist WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", trackId);
                cmd.Parameters.AddWithValue("@title", newTitle);
                cmd.Parameters.AddWithValue("@artist", newArtist);
                cmd.ExecuteNonQuery();
            }
        });
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var res = cmd.ExecuteScalar();
        return res != null ? res.ToString() ?? defaultValue : defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }
}
