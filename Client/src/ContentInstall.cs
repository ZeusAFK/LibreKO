using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using HttpClient = System.Net.Http.HttpClient;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public enum ContentStage
{
    Idle, Checking, NeedsConsent, Downloading, Verifying, Mounting, Done, Failed, AppOutdated,
}

public sealed class ContentInstall
{
    public const string ChannelPath = "android/live.json";
    public const long ConsentThreshold = 200L << 20;
    private const int ChunkBytes = 1 << 18;
    private const int SpeedWindowMs = 3000;
    private const int StallAttempts = 6;
    private const int MaxRetryDelaySeconds = 30;
    private const int StallSeconds = 20;

    public string AppUpdateUrl { get; private set; } = "";

    private readonly object _gate = new();
    private ContentStage _stage = ContentStage.Idle;
    private string _detail = "";
    private long _done, _total;
    private double _bytesPerSecond;
    private long _windowBytes;
    private long _windowStartMs;
    private long _lastProgressMs;
    private TaskCompletionSource? _consent;

    public string BaseUrl { get; }

    public ContentInstall(string baseUrl) =>
        BaseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

    public readonly record struct Snapshot(
        ContentStage Stage, string Detail, long Done, long Total, double BytesPerSecond, long IdleMs);

    public Snapshot Read()
    {
        lock (_gate)
            return new Snapshot(_stage, _detail, _done, _total, _bytesPerSecond,
                                (long)Time.GetTicksMsec() - _lastProgressMs);
    }

    private void Set(ContentStage stage, string detail)
    {
        lock (_gate) { _stage = stage; _detail = detail; }
    }

    private void Reset(long total)
    {
        lock (_gate)
        {
            _done = 0; _total = total; _bytesPerSecond = 0;
            _windowBytes = 0; _windowStartMs = 0;
            _lastProgressMs = (long)Time.GetTicksMsec();
        }
    }

    private void Advance(long done, long total)
    {
        lock (_gate)
        {
            long now = (long)Time.GetTicksMsec();
            if (done > _done) _lastProgressMs = now;
            if (_windowStartMs == 0) _windowStartMs = now;
            _windowBytes += done - _done;
            long span = now - _windowStartMs;
            if (span >= SpeedWindowMs)
            {
                _bytesPerSecond = _windowBytes * 1000.0 / span;
                _windowBytes = 0;
                _windowStartMs = now;
            }
            _done = done;
            _total = total;
        }
    }

    public void GiveConsent() => _consent?.TrySetResult();

    internal void Preview(ContentStage stage, string detail, long done, long total, double perSecond)
    {
        lock (_gate)
        {
            _stage = stage; _detail = detail;
            _done = done; _total = total; _bytesPerSecond = perSecond;
        }
    }

    private async Task AskConsent(long have, long total, CancellationToken token)
    {
        _consent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Reset(total);
        Advance(have, total);
        Set(ContentStage.NeedsConsent, have > 0
            ? "Picking up where the last download stopped. Use Wi-Fi if you can."
            : "This download is large. Use Wi-Fi if you can — it resumes if interrupted.");
        using (token.Register(() => _consent.TrySetCanceled(token)))
            await _consent.Task;
    }

    public Task RunAsync(CancellationToken token) => Task.Run(() => Run(token), token);

    private async Task Run(CancellationToken token)
    {
        try
        {
            string contentDir = ProjectSettings.GlobalizePath(Packs.DownloadedContentDir);
            string patchDir = ProjectSettings.GlobalizePath(Packs.DownloadedPatchDir);
            Directory.CreateDirectory(contentDir);
            Directory.CreateDirectory(patchDir);

            Set(ContentStage.Checking, "Contacting patch server");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var channel = ContentChannel.Parse(await http.GetStringAsync(BaseUrl + ChannelPath, token));
            if (channel == null) { Set(ContentStage.Failed, "Patch server sent an unreadable channel"); return; }

            if (Build.ApkBuild < channel.MinApkBuild)
            {
                AppUpdateUrl = channel.ApkUrl.Length > 0
                    ? (channel.ApkUrl.StartsWith("http") ? channel.ApkUrl : BaseUrl + channel.ApkUrl)
                    : "";
                Set(ContentStage.AppOutdated,
                    $"A newer app version is required (this app is build {Build.ApkBuild}, "
                    + $"the server needs {channel.MinApkBuild}).");
                return;
            }

            var manifest = ContentManifest.Parse(await http.GetStringAsync(BaseUrl + channel.Manifest, token));
            if (manifest == null) { Set(ContentStage.Failed, "Patch server sent an unreadable content list"); return; }

            int installed = ContentState.InstalledBuild();
            if (installed == manifest.Build && Packs.ContentReady)
            {
                Set(ContentStage.Done, "Ready");
                return;
            }

            var step = manifest.PatchFrom(installed);
            if (step != null)
                await ApplyPatch(http, step, patchDir, manifest.Build, token);
            else
                await ApplyBase(http, manifest, contentDir, patchDir, token);
        }
        catch (OperationCanceledException)
        {
            Set(ContentStage.Idle, "Paused");
        }
        catch (Exception e)
        {
            Set(ContentStage.Failed, Explain(e));
        }
    }

    private async Task ApplyBase(HttpClient http, ContentManifest manifest,
                                 string contentDir, string patchDir, CancellationToken token)
    {
        string target = Path.Combine(contentDir, "content.pck");
        string part = target + ".part";
        long have = PartLength(part);
        if (manifest.Base.Size - have > ConsentThreshold)
            await AskConsent(have, manifest.Base.Size, token);

        await Fetch(http, manifest.Base, target, "Downloading game content", token);

        foreach (var stale in Directory.EnumerateFiles(patchDir, "*.pck"))
            File.Delete(stale);

        ContentState.Save(manifest.Build);
        Set(ContentStage.Done, "Ready");
    }

    private async Task ApplyPatch(HttpClient http, ContentPatch step, string patchDir,
                                  int build, CancellationToken token)
    {
        await Fetch(http, step, Path.Combine(patchDir, $"{build:D5}.pck"), "Downloading update", token);
        ContentState.Save(build);
        Set(ContentStage.Done, "Ready");
    }

    private async Task Fetch(HttpClient http, ContentPatch what, string target, string label,
                             CancellationToken token)
    {
        string part = target + ".part";
        Reset(what.Size);

        if (File.Exists(target) && new FileInfo(target).Length == what.Size)
        {
            Set(ContentStage.Verifying, "Checking existing files");
            if (Sha256(target, token) == what.Sha256) return;
            File.Delete(target);
        }

        long have = PartLength(part);
        if (have > what.Size) { File.Delete(part); have = 0; }
        Advance(have, what.Size);
        Set(ContentStage.Downloading, have > 0 ? "Resuming download" : label);

        for (int attempt = 1; have < what.Size; attempt++)
        {
            try
            {
                long before = have;
                await Download(http, BaseUrl + what.Url, part, have, what.Size, token);
                have = PartLength(part);
                if (have >= what.Size) break;
                if (have > before) attempt = 0;
                else if (attempt >= StallAttempts)
                    throw new IOException("the transfer kept stopping short");
            }
            catch (Exception e) when (attempt < StallAttempts && Transient(e)
                                      && !token.IsCancellationRequested)
            {
                long reached = PartLength(part);
                if (reached > have) attempt = 0;
                have = reached;
                Advance(have, what.Size);
                Set(ContentStage.Downloading,
                    $"Connection lost — retrying ({attempt + 1} of {StallAttempts})");
                await Task.Delay(RetryDelay(attempt), token);
                Set(ContentStage.Downloading, "Resuming download");
            }
        }

        Set(ContentStage.Verifying, "Verifying download");
        if (Sha256(part, token) != what.Sha256)
        {
            File.Delete(part);
            throw new InvalidDataException("checksum mismatch");
        }

        Set(ContentStage.Mounting, "Installing");
        if (File.Exists(target)) File.Delete(target);
        File.Move(part, target);
    }

    private async Task Download(HttpClient http, string url, string part, long from, long total,
                                CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, null);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        var window = TimeSpan.FromSeconds(StallSeconds);
        stall.CancelAfter(window);

        try
        {
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (from > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            {
                from = 0;
                if (File.Exists(part)) File.Delete(part);
            }
            response.EnsureSuccessStatusCode();

            await using var net = await response.Content.ReadAsStreamAsync(stall.Token);
            await using var file = new FileStream(part, from > 0 ? FileMode.Append : FileMode.Create,
                                                  System.IO.FileAccess.Write, FileShare.None, ChunkBytes);
            var buffer = new byte[ChunkBytes];
            long done = from;
            int read;
            stall.CancelAfter(window);
            while ((read = await net.ReadAsync(buffer, stall.Token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                Advance(done, total);
                stall.CancelAfter(window);
            }
            await file.FlushAsync(token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"no data arrived for {StallSeconds} seconds");
        }
    }

    private static long PartLength(string part) =>
        File.Exists(part) ? new FileInfo(part).Length : 0;

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(MaxRetryDelaySeconds, 1 << Math.Clamp(attempt, 1, 5)));

    private static bool Transient(Exception e)
    {
        for (Exception? x = e; x != null; x = x.InnerException)
            if (x is HttpRequestException or IOException or System.Net.Sockets.SocketException
                or OperationCanceledException or TimeoutException)
                return true;
        return false;
    }

    private string Sha256(string path, CancellationToken token)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var buffer = new byte[ChunkBytes];
        long done = 0, total = stream.Length;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
            done += read;
            Advance(done, total);
        }
        sha.TransformFinalBlock(buffer, 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static string Explain(Exception e)
    {
        for (Exception? x = e; x != null; x = x.InnerException)
        {
            if (x is InvalidDataException) return "The download was damaged — tap Retry to fetch it again";
            if (x is IOException space && space.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
                return "Not enough free space on this device";
            if (x is HttpRequestException or IOException or System.Net.Sockets.SocketException)
                return "The download was interrupted — tap Retry to continue";
            if (x is TaskCanceledException or TimeoutException)
                return "The patch server stopped responding — tap Retry to continue";
        }
        return e.Message;
    }
}

public sealed class ContentPatch
{
    public int From { get; init; }
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";

    public static ContentPatch? Parse(Godot.Collections.Dictionary d)
    {
        string url = d.TryGetValue("url", out var u) ? u.AsString() : "";
        if (url.Length == 0) return null;
        return new ContentPatch
        {
            From = d.TryGetValue("from", out var f) ? f.AsInt32() : 0,
            Url = url,
            Size = d.TryGetValue("size", out var s) ? s.AsInt64() : 0,
            Sha256 = (d.TryGetValue("sha256", out var h) ? h.AsString() : "").ToLowerInvariant(),
        };
    }
}

public sealed class ContentChannel
{
    public int Build { get; init; }
    public int MinApkBuild { get; init; }
    public string Manifest { get; init; } = "";
    public string ApkUrl { get; init; } = "";

    public static ContentChannel? Parse(string json)
    {
        if (Json.ParseString(json).AsGodotDictionary() is not { } d || d.Count == 0) return null;
        string manifest = d.TryGetValue("manifest", out var m) ? m.AsString() : "";
        if (manifest.Length == 0) return null;
        return new ContentChannel
        {
            Build = d.TryGetValue("build", out var b) ? b.AsInt32() : 0,
            MinApkBuild = d.TryGetValue("minApkBuild", out var a) ? a.AsInt32() : 0,
            Manifest = manifest,
            ApkUrl = d.TryGetValue("apkUrl", out var k) ? k.AsString() : "",
        };
    }
}

public sealed class ContentManifest
{
    public int Build { get; init; }
    public ContentPatch Base { get; init; } = new();
    public List<ContentPatch> Patches { get; init; } = new();

    public ContentPatch? PatchFrom(int installed)
    {
        if (installed <= 0) return null;
        foreach (var p in Patches)
            if (p.From == installed) return p;
        return null;
    }

    public static ContentManifest? Parse(string json)
    {
        if (Json.ParseString(json).AsGodotDictionary() is not { } d || d.Count == 0) return null;
        if (!d.TryGetValue("base", out var b)) return null;
        if (ContentPatch.Parse(b.AsGodotDictionary()) is not { } baseline) return null;

        var patches = new List<ContentPatch>();
        if (d.TryGetValue("patches", out var raw))
            foreach (var item in raw.AsGodotArray())
                if (ContentPatch.Parse(item.AsGodotDictionary()) is { } p) patches.Add(p);

        return new ContentManifest
        {
            Build = d.TryGetValue("build", out var n) ? n.AsInt32() : 0,
            Base = baseline,
            Patches = patches,
        };
    }
}

public static class ContentState
{
    private const string Path = "user://content/installed.json";

    public static int InstalledBuild()
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (f == null) return 0;
        return Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d
               && d.TryGetValue("build", out var b) ? b.AsInt32() : 0;
    }

    public static void Save(int build)
    {
        DirAccess.MakeDirRecursiveAbsolute(Packs.DownloadedContentDir);
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString($"{{\"build\":{build}}}");
    }
}
