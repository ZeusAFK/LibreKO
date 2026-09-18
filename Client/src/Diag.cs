using System;
using System.Text;
using Godot;

namespace LibreKO;

public static class Diag
{
    private const int MaxLogBytes = 1 << 20;
    private const int TraceRing = 64;

    private static readonly object Gate = new();
    private static readonly string[] _trace = new string[TraceRing];
    private static int _traceAt;
    private static string _path = "";
    private static bool _installed;

    public static string Phase { get; set; } = "startup";
    public static bool SlowLog { get; set; }

    public static System.Diagnostics.Stopwatch? Watch() =>
        SlowLog ? System.Diagnostics.Stopwatch.StartNew() : null;

    public static void Slow(string what, System.Diagnostics.Stopwatch? watch, double thresholdMs = 4)
    {
        if (watch == null) return;
        double ms = watch.Elapsed.TotalMilliseconds;
        if (ms >= thresholdMs) GD.Print($"[slow] {what} {ms:0.0}ms");
    }

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            _installed = true;
            _path = System.IO.Path.Combine(OS.GetUserDataDir(), "faults.log");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Report("appdomain", a.ExceptionObject as Exception, a.IsTerminating ? "TERMINATING" : null);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Report("task", a.Exception);
            a.SetObserved();
        };
    }

    public static void Step(string what)
    {
        lock (Gate)
        {
            _trace[_traceAt] = $"{DateTime.Now:HH:mm:ss.fff} {what}";
            _traceAt = (_traceAt + 1) % TraceRing;
        }
    }

    public static async System.Threading.Tasks.Task Guard(
        string where, Func<System.Threading.Tasks.Task> body)
    {
        try { await body(); }
        catch (Exception e) { Report(where, e); }
    }

    public static void Report(string where, Exception? e, string? note = null)
    {
        string text;
        lock (Gate)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                .Append(where)
                .Append(" phase=").Append(Phase)
                .Append(" thread=").Append(System.Environment.CurrentManagedThreadId);
            if (note != null) sb.Append(' ').Append(note);
            sb.AppendLine();
            for (var x = e; x != null; x = x.InnerException)
            {
                sb.Append("  ").Append(x.GetType().FullName).Append(": ").AppendLine(x.Message);
                if (x.StackTrace != null) sb.AppendLine(x.StackTrace);
            }
            for (int i = 0; i < TraceRing; i++)
            {
                string? line = _trace[(_traceAt + i) % TraceRing];
                if (line != null) sb.Append("  · ").AppendLine(line);
            }
            text = sb.ToString();
        }
        Append(text);
        try { GD.PushError(text); } catch { }
    }

    private static void Append(string text)
    {
        if (_path.Length == 0) return;
        try
        {
            var info = new System.IO.FileInfo(_path);
            if (info.Exists && info.Length > MaxLogBytes) info.Delete();
            System.IO.File.AppendAllText(_path, text + System.Environment.NewLine);
        }
        catch { }
    }
}
