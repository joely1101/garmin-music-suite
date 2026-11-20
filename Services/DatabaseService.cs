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
            var folders = new List<FolderItem>();
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT folder, COUNT(*) FROM tracks GROUP BY folder ORDER BY folder ASC";
            using var reader = cmd.ExecuteReader();
            int total = 0;
            while (reader.Read())
            {
                var f = reader.GetString(0);
                var c = reader.GetInt32(1);
                total += c;
                folders.Add(new FolderItem { Name = f, Count = c });
            }

            // Ensure "音樂" exists
            if (!folders.Exists(x => x.Name == "音樂"))
            {
                folders.Insert(0, new FolderItem { Name = "音樂", Count = 0 });
            }

            // Insert "全部歌曲"
            folders.Insert(0, new FolderItem { Name = "全部歌曲", Count = total });
            return folders;
        });
    }

    public void CreateFolder(string folderName)
    {
        var clean = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(clean)) return;
        var dir = Path.Combine(MusicDir, clean);
        Directory.CreateDirectory(dir);
    }

    public async Task DeleteTrackAsync(string trackId)
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
                    try { File.Delete(fullPath); } catch { }
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
