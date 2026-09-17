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

    private static readonly List<Entry> _live = new();
    private static int _pruneAt = 512;

    public static T? Track<T>(T? node, string name, string kind) where T : Node3D
    {
        if (node == null) return null;
        if (_live.Count >= _pruneAt) Prune();
        _live.Add(new Entry { Node = node, Name = name, Kind = kind });
        return node;
    }

    public static void Prune()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
            if (!GodotObject.IsInstanceValid(_live[i].Node)) _live.RemoveAt(i);
        _pruneAt = Mathf.Max(512, _live.Count * 2);
    }

    public static void Clear()
    {
        _live.Clear();
        _pruneAt = 512;
    }

    public static List<Entry> Live
    {
        get { Prune(); return _live; }
    }
}
