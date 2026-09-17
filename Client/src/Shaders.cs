using Godot;

namespace LibreKO;

public static class Shaders
{
    private static readonly System.Collections.Generic.Dictionary<string, Shader> _cache = new();

    public static Shader Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;
        string path = $"res://shaders/{name}.gdshader";
        var shader = ResourceLoader.Load<Shader>(path);
        if (shader == null) GD.PushError($"[shaders] missing {path}");
        _cache[name] = shader!;
        return shader!;
    }

    public static ShaderMaterial Material(string name) => new() { Shader = Get(name) };
}
