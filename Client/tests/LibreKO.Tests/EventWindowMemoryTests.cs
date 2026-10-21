using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class EventWindowMemoryTests
{
    private const string Key = "7:3";

    [Fact]
    public void TheSameRunKeepsWhatThePlayerChose()
    {
        var memory = new EventWindowMemory();
        Assert.True(memory.Observe(Key, 100, 600));
        memory.SetFolded(Key, true);
        memory.SetHidden(Key, true);

        Assert.False(memory.Observe(Key, 160, 540));
        Assert.True(memory.Folded(Key));
        Assert.True(memory.Hidden(Key));
    }

    [Fact]
    public void AMovedEndTimeIsANewRun()
    {
        var memory = new EventWindowMemory();
        memory.Observe(Key, 100, 600);
        memory.SetFolded(Key, true);

        Assert.True(memory.Observe(Key, 160, 1800));
        Assert.False(memory.Folded(Key));
    }

    [Fact]
    public void ARunThatEndedLongAgoStartsOver()
    {
        var memory = new EventWindowMemory();
        memory.Observe(Key, 100, 60);
        memory.SetHidden(Key, true);

        Assert.True(memory.Observe(Key, 100 + 60 + EventWindowMemory.EndedGraceSeconds + 1, 0));
        Assert.False(memory.Hidden(Key));
    }
}
