using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LibreKO.Domain;

public readonly record struct EmoticonInfo(int Id, string Code, int X, int Y);

public readonly record struct EmoticonSegment(string Text, int Emoticon);

public static class Emoticons
{
    public const string SheetPath = "res://assets/ui/emoticons/sheet.png";
    private const string IndexPath = "res://assets/ui/emoticons/emoticons.json";
    private const char Open = '(';
    private const char Close = ')';
    private const int DefaultCell = 20;

    private static readonly Dictionary<string, EmoticonInfo> ByCode = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, EmoticonInfo> ById = new();
    private static readonly List<EmoticonInfo> Ordered = new();
    private static bool loaded;

    public static int Cell { get; private set; } = DefaultCell;

    public static IReadOnlyList<EmoticonInfo> All
    {
        get
        {
            EnsureLoaded();
            return Ordered;
        }
    }

    public static EmoticonInfo? Get(int id)
    {
        EnsureLoaded();
        return ById.TryGetValue(id, out var info) ? info : null;
    }

    public static void Seed(IEnumerable<EmoticonInfo> emoticons, int cell = DefaultCell)
    {
        ByCode.Clear();
        ById.Clear();
        Ordered.Clear();
        Cell = cell;
        foreach (var e in emoticons)
        {
            ByCode[e.Code] = e;
            ById[e.Id] = e;
            Ordered.Add(e);
        }
        loaded = true;
    }

    public static bool Contains(string text)
    {
        foreach (var segment in Split(text))
            if (segment.Emoticon > 0) return true;
        return false;
    }

    public static List<EmoticonSegment> Split(string text)
    {
        EnsureLoaded();
        var parts = new List<EmoticonSegment>();
        int start = 0;
        int i = 0;
        while (i < text.Length)
        {
            int close = text[i] == Open ? text.IndexOf(Close, i + 1) : -1;
            if (close > i && ByCode.TryGetValue(text[i..(close + 1)], out var info))
            {
                if (i > start) parts.Add(new EmoticonSegment(text[start..i], 0));
                parts.Add(new EmoticonSegment(info.Code, info.Id));
                i = close + 1;
                start = i;
                continue;
            }
            i++;
        }
        if (start < text.Length) parts.Add(new EmoticonSegment(text[start..], 0));
        return parts;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        if (GameFiles.ReadText(IndexPath) is not { } index) return;
        using var doc = JsonDocument.Parse(index);
        int cell = doc.RootElement.TryGetProperty("cell", out var c) ? c.GetInt32() : DefaultCell;
        var list = new List<EmoticonInfo>();
        foreach (var e in doc.RootElement.GetProperty("emoticons").EnumerateArray())
            list.Add(new EmoticonInfo(e.GetProperty("id").GetInt32(), e.GetProperty("code").GetString() ?? "",
                e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32()));
        Seed(list, cell);
    }
}
