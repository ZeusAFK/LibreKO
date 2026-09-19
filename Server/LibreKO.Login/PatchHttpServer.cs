using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Login;

public class PatchHttpServer : BackgroundService
{
    public const int DefaultPort = 15150;
    private readonly int _port;
    private readonly string _contentDir;
    private readonly ILogger<PatchHttpServer> _logger;
    private TcpListener? _listener;

    public PatchHttpServer(ILogger<PatchHttpServer> logger)
    {
        _logger = logger;
        _port = DefaultPort;

        string[] candidates =
        [
            Path.GetFullPath(@"D:\LibreKO\PlayableClient\LibreKO\content"),
            Path.Combine(AppContext.BaseDirectory, "content"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\PlayableClient\LibreKO\content")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\PlayableClient\LibreKO\content"))
        ];

        _contentDir = candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        _logger.LogInformation("PatchHttpServer content directory configured at: {Dir}", _contentDir);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _logger.LogInformation("HTTP Patch Server started on port {Port} serving: {ContentDir}", _port, _contentDir);

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP Patch Server error");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 60000;
            stream.WriteTimeout = 60000;

            try
            {
                var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(token);
                if (string.IsNullOrEmpty(requestLine)) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0];
                var url = parts[1].Split('?')[0];

                long? rangeFrom = null;
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(token)))
                {
                    if (headerLine.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        var rangeVal = headerLine["Range: bytes=".Length..].Trim();
                        var dashIdx = rangeVal.IndexOf('-');
                        if (dashIdx > 0 && long.TryParse(rangeVal[..dashIdx], out var from))
                        {
                            rangeFrom = from;
                        }
                    }
                }

                if (method != "GET" && method != "HEAD")
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain", "Method Not Allowed");
                    return;
                }

                if (url == "/android/live.json")
                {
                    var live = new
                    {
                        build = 1,
                        minApkBuild = 0,
                        manifest = "android/manifest.json",
                        apkUrl = ""
                    };
                    var json = JsonSerializer.Serialize(live, new JsonSerializerOptions { WriteIndented = true });
                    await WriteResponseAsync(stream, 200, "OK", "application/json", json);
                    return;
                }

                if (url == "/android/manifest.json")
                {
                    string[] packNames = ["terrain.pck", "characters.pck", "armor.pck", "weapons.pck", "npcs.pck", "objects.pck"];
                    var packs = new List<object>();

                    foreach (var pck in packNames)
                    {
                        var filePath = Path.Combine(_contentDir, pck);
                        long size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
                        packs.Add(new
                        {
                            url = $"android/pcks/{pck}",
                            size = size,
                            sha256 = ""
                        });
                    }

                    var manifest = new
                    {
                        build = 1,
                        basePatch = packs.Count > 0 ? packs[0] : null,
                        packs = packs,
                        patches = Array.Empty<object>()
                    };

                    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                    await WriteResponseAsync(stream, 200, "OK", "application/json", json);
                    return;
                }

                if (url.StartsWith("/android/pcks/"))
                {
                    var fileName = Path.GetFileName(url);
                    var filePath = Path.Combine(_contentDir, fileName);

                    if (!File.Exists(filePath))
                    {
                        await WriteResponseAsync(stream, 404, "Not Found", "text/plain", $"File {fileName} not found");
                        return;
                    }

                    await ServeFileAsync(stream, filePath, rangeFrom, method == "HEAD", token);
                    return;
                }

                await WriteResponseAsync(stream, 404, "Not Found", "text/plain", "Not Found");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling HTTP patch client");
            }
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int statusCode, string statusText, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {bytes.Length}\r\n" +
                     "Connection: close\r\n" +
                     "Access-Control-Allow-Origin: *\r\n\r\n";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private async Task ServeFileAsync(Stream stream, string filePath, long? rangeFrom, bool headOnly, CancellationToken token)
    {
        var fileInfo = new FileInfo(filePath);
        var totalLength = fileInfo.Length;

        long start = rangeFrom ?? 0;
        if (start >= totalLength) start = 0;
        long contentLength = totalLength - start;

        int statusCode = rangeFrom.HasValue && rangeFrom.Value > 0 ? 206 : 200;
        string statusText = statusCode == 206 ? "Partial Content" : "OK";

        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        headerBuilder.Append("Content-Type: application/octet-stream\r\n");
        headerBuilder.Append($"Content-Length: {contentLength}\r\n");
        headerBuilder.Append("Accept-Ranges: bytes\r\n");
        headerBuilder.Append("Access-Control-Allow-Origin: *\r\n");

        if (statusCode == 206)
        {
            headerBuilder.Append($"Content-Range: bytes {start}-{totalLength - 1}/{totalLength}\r\n");
        }

        headerBuilder.Append("Connection: close\r\n\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
        await stream.WriteAsync(headerBytes, token);

        if (headOnly)
        {
            await stream.FlushAsync(token);
            return;
        }

        _logger.LogInformation("Serving {File} from byte {Start}/{Total} ({Length:n0} bytes)...",
            fileInfo.Name, start, totalLength, contentLength);

        await using var fileStream = new FileStream(filePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, 65536);
        if (start > 0)
        {
            fileStream.Seek(start, SeekOrigin.Begin);
        }

        var buffer = new byte[65536];
        int read;
        while ((read = await fileStream.ReadAsync(buffer, token)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), token);
        }

        await stream.FlushAsync(token);
        _logger.LogInformation("Completed sending {File}", fileInfo.Name);
    }
}
