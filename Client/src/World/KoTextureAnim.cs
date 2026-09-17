using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class KoTextureAnim
{
    private const string Marker = "_a";

    private sealed class Track
    {
        public required StandardMaterial3D Material;
        public required Texture2D[] Frames;
        public required float Fps;
    }

    private static readonly List<Track> Tracks = new();
    private static double _time;

    public static int Count => Tracks.Count;

    public static void Reset()
    {
        Tracks.Clear();
        _time = 0;
    }

    public static (float Fps, int Frames) Parse(string materialName, int stateEnd)
    {
        int marker = materialName.IndexOf(Marker, stateEnd, StringComparison.Ordinal);
        if (marker < 0) return (0f, 0);
        int cross = materialName.IndexOf('x', marker + Marker.Length);
        if (cross < 0) return (0f, 0);
        if (!float.TryParse(materialName.AsSpan(marker + Marker.Length, cross - marker - Marker.Length),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float fps)
            || !int.TryParse(materialName.AsSpan(cross + 1), out int frames)
            || fps <= 0f || frames < 2)
            return (0f, 0);
        return (fps, frames);
    }

    public static void Register(StandardMaterial3D material, float fps, int frameCount)
    {
        if (material.AlbedoTexture is not { } first) return;
        string path = first.ResourcePath;
        if (!path.EndsWith(".png", StringComparison.Ordinal)) return;

        string stem = BaseStem(path[..^4]);
        var frames = new Texture2D[frameCount];
        if (ResourceLoader.Load($"{stem}.png") is not Texture2D first0) return;
        frames[0] = first0;
        for (int i = 1; i < frameCount; i++)
        {
            if (ResourceLoader.Load($"{stem}_f{i}.png") is not Texture2D tex) return;
            frames[i] = tex;
        }
        Tracks.Add(new Track { Material = material, Frames = frames, Fps = fps });
    }

    private static string BaseStem(string stem)
    {
        int marker = stem.LastIndexOf("_f", StringComparison.Ordinal);
        if (marker < 0) return stem;
        for (int i = marker + 2; i < stem.Length; i++)
            if (!char.IsAsciiDigit(stem[i])) return stem;
        return marker + 2 < stem.Length ? stem[..marker] : stem;
    }

    public static string Describe()
    {
        if (Tracks.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var track in Tracks)
        {
            int frame = (int)(_time * track.Fps) % track.Frames.Length;
            parts.Add($"{frame}/{track.Frames.Length}@{track.Fps:g}");
        }
        return string.Join(" ", parts);
    }

    public static void Tick(double delta)
    {
        if (Tracks.Count == 0) return;
        _time += delta;
        foreach (var track in Tracks)
        {
            if (!GodotObject.IsInstanceValid(track.Material)) continue;
            var tex = track.Frames[(int)(_time * track.Fps) % track.Frames.Length];
            if (track.Material.AlbedoTexture != tex)
                track.Material.AlbedoTexture = tex;
        }
    }
}
