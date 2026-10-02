using System;
using System.Collections;
using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public static class Shutdown
{
    private const double FadeSeconds = 1.2;
    private const int WatchdogMs = 6000;
    private const int TeardownFrames = 2;

    private static bool _started;
    private static readonly List<Action> Releases = new();

    internal static T Track<T>(T cache) where T : class, IDictionary
    {
        Releases.Add(() =>
        {
            foreach (var value in cache.Values) Release(value);
            cache.Clear();
        });
        return cache;
    }

    internal static Dictionary<TKey, TValue> Track<TKey, TValue>(Dictionary<TKey, TValue> cache, Func<TValue, object?> held)
        where TKey : notnull
    {
        Releases.Add(() =>
        {
            foreach (var value in cache.Values) Release(held(value));
            cache.Clear();
        });
        return cache;
    }

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
        tree.CurrentScene?.QueueFree();
        foreach (var child in tree.Root.GetChildren())
            if (child is FxLayer) child.QueueFree();
        await Frames(tree, TeardownFrames);
        foreach (var release in Releases) release();
        await Frames(tree, TeardownFrames);
        tree.Quit();
    }

    private static async System.Threading.Tasks.Task Frames(SceneTree tree, int count)
    {
        for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static void Release(object? value)
    {
        switch (value)
        {
            case Node node:
                if (GodotObject.IsInstanceValid(node) && !node.IsInsideTree()) node.Free();
                break;
            case GodotObject resource:
                if (GodotObject.IsInstanceValid(resource)) resource.Dispose();
                break;
            case System.Runtime.CompilerServices.ITuple tuple:
                for (int i = 0; i < tuple.Length; i++) Release(tuple[i]);
                break;
        }
    }
}
