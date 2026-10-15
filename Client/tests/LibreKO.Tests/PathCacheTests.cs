using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PathCacheTests
{
    [Fact]
    public void EachPathLoadsOnceAndKeepsWhatItGot()
    {
        var cache = new PathCache<string>();
        var loads = new List<string>();
        string? Load(string path) { loads.Add(path); return path.ToUpperInvariant(); }

        Assert.Equal("A.GLB", cache.Get("a.glb", Load));
        Assert.Equal("A.GLB", cache.Get("a.glb", Load));
        Assert.Equal("B.GLB", cache.Get("b.glb", Load));

        Assert.Equal(new[] { "a.glb", "b.glb" }, loads);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void AMissingFileIsRememberedAsMissing()
    {
        var cache = new PathCache<string>();
        int loads = 0;
        string? Missing(string path) { loads++; return null; }

        Assert.Null(cache.Get("gone.plugs.json", Missing));
        Assert.Null(cache.Get("gone.plugs.json", Missing));

        Assert.Equal(1, loads);
    }

    [Fact]
    public void ClearingLoadsAgain()
    {
        var cache = new PathCache<string>();
        int loads = 0;
        string? Load(string path) { loads++; return path; }
        cache.Get("x", Load);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        cache.Get("x", Load);
        Assert.Equal(2, loads);
    }
}
