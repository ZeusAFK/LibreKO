using Godot;

namespace LibreKO.Presentation;

public static class NodeSearch
{
    public static T? FindFirst<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
        {
            var r = FindFirst<T>(c);
            if (r != null) return r;
        }
        return null;
    }
}
