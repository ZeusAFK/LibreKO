using FluentAssertions;
using LibreKO.Quests;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestIncludeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "kq-inc-" + Guid.NewGuid().ToString("N"));

    public QuestIncludeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "locations"));
        Directory.CreateDirectory(Path.Combine(_root, "scripts"));
        File.WriteAllText(
            Path.Combine(_root, "locations", "global.quest"),
            "country_cont = location \"Country CONT\" found \"War zone\"\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("file:///d:/quests/a.quest", @"d:\quests\a.quest")]
    [InlineData("file:///d%3A/quests/a.quest", @"d:\quests\a.quest")]
    [InlineData("file:///D%3A/quests/a.quest", @"D:\quests\a.quest")]
    [InlineData("file:///d%3A/quests/with%20space/a.quest", @"d:\quests\with space\a.quest")]
    public void LocalPath_DecodesTheFormsEditorsSend(string uri, string expected)
    {
        QuestUri.LocalPath(uri).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("untitled:Untitled-1")]
    [InlineData("not a uri at all")]
    public void LocalPath_IsNullForAnythingThatIsNotAFile(string uri)
    {
        QuestUri.LocalPath(uri).Should().BeNull();
    }

    [Fact]
    public void AnIncludeResolvesFromAnAncestorDirectory()
    {
        var includes = new DirectoryQuestIncludes(Path.Combine(_root, "scripts"));

        includes.TryRead("locations/global", out var text, out var fileName)
            .Should().BeTrue("a script in a subdirectory should reach locations/ without '../'");
        fileName.Should().Be(Path.Combine(_root, "locations", "global.quest"));
        text.Should().Contain("Country CONT");
    }

    [Fact]
    public void NestedLibrariesUseTheirOwnDirectoryAndDistinctFilesMayShareABasename()
    {
        Directory.CreateDirectory(Path.Combine(_root, "translations"));
        File.WriteAllText(Path.Combine(_root, "translations", "words.quest"), "hello = text \"Hello\"");
        File.WriteAllText(Path.Combine(_root, "translations", "global.quest"),
            "include words\ninclude locations/global");
        var path = Path.Combine(_root, "scripts", "global.quest");
        File.WriteAllText(path, "include translations/global\nBind Npc 100\nOn greeting\n    Say hello");
        var compilation = QuestCompilation.CreateFromFile(path);
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Program.Locations.Should().ContainSingle();
    }

    [Fact]
    public void AnIncludeThatEscapesTheRootIsRefused()
    {
        var includes = new DirectoryQuestIncludes(Path.Combine(_root, "scripts"));

        includes.TryRead("../locations/global", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AMissingIncludeIsReportedAgainstTheIncludeLine()
    {
        var script = string.Join('\n',
            "include locations/nope",
            "",
            "Bind Npc 16079",
            "",
            "g_topic = event 1205",
            "",
            "On g_topic",
            "    Say \"hi\"",
            "    Topic \"x\" goto close");
        var path = Path.Combine(_root, "scripts", "a.quest");
        File.WriteAllText(path, script);

        var compilation = QuestCompilation.CreateFromFile(path);

        compilation.Succeeded.Should().BeFalse();
        var error = compilation.Diagnostics.Single(d => d.Id == Quests.Text.DiagnosticId.IncludeNotFound);
        compilation.Source.GetLinePosition(error.Span.Start).Line.Should().Be(0);
    }

    [Fact]
    public void ANameFromAnIncludedFileResolves()
    {
        var script = string.Join('\n',
            "include locations/global",
            "",
            "Bind Npc 16079",
            "",
            "g_topic = event 1205",
            "",
            "On g_topic",
            "    Map country_cont");
        var path = Path.Combine(_root, "scripts", "b.quest");
        File.WriteAllText(path, script);

        var compilation = QuestCompilation.CreateFromFile(path);

        compilation.Diagnostics
            .Where(d => d.Severity == Quests.Text.DiagnosticSeverity.Error)
            .Should().BeEmpty();
        compilation.Program.Locations.Should().ContainSingle()
            .Which.Title.Should().Be("Country CONT");
    }
}
