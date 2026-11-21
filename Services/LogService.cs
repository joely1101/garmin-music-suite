using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GarminMusicStudio.Services;

public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private readonly object _lock = new();
    public string LogFilePath { get; }
    public string LogDirectory { get; }

    public LogService()
    {
        try
        {
            LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
            LogFilePath = Path.Combine(LogDirectory, "mtp_debug.log");
        }
        catch
        {
            LogDirectory = AppDomain.CurrentDomain.BaseDirectory;
            LogFilePath = Path.Combine(LogDirectory, "mtp_debug.log");
        }
    }

    public void LogInfo(string message) => WriteLog("INFO", message);
    public void LogWarn(string message) => WriteLog("WARN", message);
    public void LogError(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        WriteLog("ERROR", msg);
    }

    private void WriteLog(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        Debug.WriteLine(line);

        lock (_lock)
        {
            try
            {
                // Simple size rotation: if log > 5MB, keep the last 1MB
                if (File.Exists(LogFilePath))
                {
                    var fi = new FileInfo(LogFilePath);
                    if (fi.Length > 5 * 1024 * 1024)
                    {
                        var backup = LogFilePath + ".old";
                        try { File.Delete(backup); } catch { }
                        try { File.Move(LogFilePath, backup); } catch { }
                    }
                }

                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    public void OpenLogFile()
    {
        try
        {
            if (!File.Exists(LogFilePath))
            {
                LogInfo("日誌檔案建立於 " + DateTime.Now);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open log file: {ex.Message}");
        }
    }

    public void OpenLogDirectory()
    {
        try
        {
            if (Directory.Exists(LogDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LogDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}
