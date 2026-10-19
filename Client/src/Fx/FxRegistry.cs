using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class FxRegistry
{
    public sealed class Entry
    {
        public Node3D Node = null!;
        public string Name = "";
        public string Kind = "";
        public Aabb? LocalBounds;
    }

    private static readonly List<Entry> live = new();
    private static int pruneAt = 512;

    public static T? Track<T>(T? node, string name, string kind) where T : Node3D
    {
        if (node == null) return null;
        if (live.Count >= pruneAt) Prune();
        live.Add(new Entry { Node = node, Name = name, Kind = kind });
        return node;
    }

    public static void Prune()
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (!GodotObject.IsInstanceValid(live[i].Node)) live.RemoveAt(i);
        pruneAt = Mathf.Max(512, live.Count * 2);
    }

    public static void Clear()
    {
        live.Clear();
        pruneAt = 512;
    }

    public static List<Entry> Live
    {
        get { Prune(); return live; }
    }
}
