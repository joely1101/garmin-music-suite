using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GarminMusicStudio.Services;

public class SubsonicServerService
{
    private static readonly Lazy<SubsonicServerService> _instance = new(() => new SubsonicServerService());
    public static SubsonicServerService Instance => _instance.Value;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    public int Port { get; private set; } = 28888;
    public bool IsRunning => _listener?.IsListening ?? false;

    public string LocalIpAddress
    {
        get
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint?.Address.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }

    public void Start(int port = 28888)
    {
        if (IsRunning) return;

        Port = port;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        try
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Subsonic HttpListener Start error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        _listener = null;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context));
            }
            catch
            {
                break;
            }
        }
    }

    private void ProcessRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        // CORS headers
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.Headers.Add("Access-Control-Allow-Headers", "*");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        var path = req.Url?.AbsolutePath ?? "/";
        var query = req.QueryString;
        var format = query["f"] ?? "xml";

        try
        {
            if (path.StartsWith("/rest/ping.view", StringComparison.OrdinalIgnoreCase))
            {
                SendSubsonicResponse(resp, "<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\" version=\"1.16.1\" type=\"garmin-subsonic\" serverVersion=\"1.0.0\"/>", format);
            }
            else if (path.StartsWith("/rest/getMusicFolders.view", StringComparison.OrdinalIgnoreCase))
            {
                SendSubsonicResponse(resp, "<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\" version=\"1.16.1\"><musicFolders><musicFolder id=\"1\" name=\"Garmin Music\"/></musicFolders></subsonic-response>", format);
            }
            else if (path.StartsWith("/rest/stream.view", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/rest/download.view", StringComparison.OrdinalIgnoreCase))
            {
                var id = query["id"] ?? "";
                StreamAudioFile(resp, id);
            }
            else
            {
                // General OK response for Subsonic compatibility
                SendSubsonicResponse(resp, "<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\" version=\"1.16.1\"/>", format);
            }
        }
        catch
        {
            resp.StatusCode = 500;
            resp.Close();
        }
    }

    private void SendSubsonicResponse(HttpListenerResponse resp, string xmlContent, string format)
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            // Simple json fallback
            var json = "{\"subsonic-response\":{\"status\":\"ok\",\"version\":\"1.16.1\",\"type\":\"garmin-subsonic\"}}";
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.OutputStream.Write(bytes, 0, bytes.Length);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(xmlContent);
            resp.ContentType = "text/xml; charset=utf-8";
            resp.OutputStream.Write(bytes, 0, bytes.Length);
        }
        resp.Close();
    }

    private void StreamAudioFile(HttpListenerResponse resp, string trackId)
    {
        var tracks = DatabaseService.Instance.GetTracksAsync().GetAwaiter().GetResult();
        var track = tracks.Find(t => t.Id == trackId || t.RelPath == trackId);
        if (track == null)
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        var fullPath = Path.Combine(DatabaseService.Instance.MusicDir, track.RelPath.Replace('/', '\\'));
        if (!File.Exists(fullPath))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        resp.ContentType = "audio/mpeg";
        var fi = new FileInfo(fullPath);
        resp.ContentLength64 = fi.Length;
        using var fs = File.OpenRead(fullPath);
        fs.CopyTo(resp.OutputStream);
        resp.Close();
    }
}
