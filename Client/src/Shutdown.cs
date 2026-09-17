using Godot;
using LibreKO.Network;

namespace LibreKO;

public static class Shutdown
{
    private const double FadeSeconds = 1.2;
    private const int WatchdogMs = 6000;

    private static bool _started;

    public static async System.Threading.Tasks.Task Begin(Node source, int layer)
    {
        if (_started) return;
        _started = true;
        Diag.Phase = "shutdown";
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(WatchdogMs);
            OS.Kill(OS.GetProcessId());
        })
        { IsBackground = true, Name = "libreko-exit-watchdog" }.Start();

        var tree = source.GetTree();
        Fx.BeginShutdown(tree.Root);

        var canvas = new CanvasLayer { Layer = layer };
        tree.Root.AddChild(canvas);
        Ui.Background(canvas, "res://assets/backgrounds/closing.jpg");
        await source.ToSignal(tree.CreateTimer(FadeSeconds), SceneTreeTimer.SignalName.Timeout);

        try { Net.I?.Disconnect(expected: true); } catch { }
        try { LoginNet.I?.Disconnect(expected: true); } catch { }

        GameCursor.Disable();
        tree.Quit();
    }
}
