using LibreKO.Plugins;
using Xunit;

namespace LibreKO.Tests;

public class PluginManifestTests
{
    private const string Full = """
        {
          "id": "knightonline-ui-utc",
          "name": "Under the Castle UI",
          "version": "0.1.0",
          "type": "ui-theme",
          "description": "The retail interface.",
          "author": "ZeusAFK",
          "homepage": "https://example.org/x",
          "assembly": "bin/KnightOnlineUi.dll",
          "entry": "KnightOnlineUi.Plugin",
          "minClientVersion": "0.1.0"
        }
        """;

    [Fact]
    public void AFullManifestReadsEveryField()
    {
        var m = PluginManifest.Parse(Full);
        Assert.Equal("knightonline-ui-utc", m.Id);
        Assert.Equal("Under the Castle UI", m.Name);
        Assert.Equal("0.1.0", m.Version);
        Assert.Equal(PluginType.UiTheme, m.Type);
        Assert.True(m.Exclusive);
        Assert.Equal("The retail interface.", m.Description);
        Assert.Equal("ZeusAFK", m.Author);
        Assert.Equal("bin/KnightOnlineUi.dll", m.Assembly);
        Assert.Equal("KnightOnlineUi.Plugin", m.Entry);
        Assert.True(m.HasCode);
        Assert.Equal(new Version(0, 1, 0), m.MinClientVersion);
    }

    [Fact]
    public void ADataOnlyManifestNeedsOnlyIdNameAndType()
    {
        var m = PluginManifest.Parse("""{"id": "notes", "name": "Notes", "type": "extension"}""");
        Assert.False(m.HasCode);
        Assert.False(m.Exclusive);
        Assert.Null(m.MinClientVersion);
        Assert.Equal("0.0.0", m.Version);
    }

    [Theory]
    [InlineData("""{"name": "x", "type": "extension"}""", "id")]
    [InlineData("""{"id": "Bad Id", "name": "x", "type": "extension"}""", "id")]
    [InlineData("""{"id": "x", "type": "extension"}""", "name")]
    [InlineData("""{"id": "x", "name": "x", "type": "widget"}""", "type")]
    [InlineData("""{"id": "x", "name": "x", "type": "extension", "assembly": "../evil.dll"}""", "assembly")]
    [InlineData("""{"id": "x", "name": "x", "type": "extension", "assembly": "bin/x.exe"}""", "assembly")]
    [InlineData("""{"id": "x", "name": "x", "type": "extension", "entry": "X.Y"}""", "entry")]
    [InlineData("""{"id": "x", "name": "x", "type": "extension", "minClientVersion": "soon"}""", "minClientVersion")]
    [InlineData("""not json""", "JSON")]
    public void ABrokenManifestNamesTheOffendingField(string json, string field)
    {
        var ex = Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(json));
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void BackslashesInTheAssemblyPathAreAccepted()
    {
        var m = PluginManifest.Parse("""{"id": "x", "name": "x", "type": "extension", "assembly": "bin\\x.dll"}""");
        Assert.Equal("bin/x.dll", m.Assembly);
    }

    [Theory]
    [InlineData("ui-theme", PluginType.UiTheme)]
    [InlineData("UI-Theme", PluginType.UiTheme)]
    [InlineData("theme", PluginType.UiTheme)]
    [InlineData("extension", PluginType.Extension)]
    public void TypeNamesAreForgiving(string name, PluginType expected)
    {
        Assert.True(PluginManifest.TryParseType(name, out var type));
        Assert.Equal(expected, type);
        Assert.True(PluginManifest.IsExclusive(PluginType.UiTheme));
        Assert.False(PluginManifest.IsExclusive(PluginType.Extension));
    }
}
