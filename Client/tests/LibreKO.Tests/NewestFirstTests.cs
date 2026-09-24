using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NewestFirstTests
{
    private sealed record Note(int Id, string Text);

    [Fact]
    public void ANewEntryGoesToTheFrontSoItIsTheOneShown()
    {
        var shown = new List<Note> { new(62, "old"), new(70, "older") };
        Assert.True(NewestFirst.Upsert(shown, new Note(63, "new"), q => q.Id));
        Assert.Equal(new[] { 63, 62, 70 }, shown.ConvertAll(q => q.Id));
    }

    [Fact]
    public void AnUpdatedEntryKeepsItsPlace()
    {
        var shown = new List<Note> { new(63, "new"), new(62, "old") };
        Assert.False(NewestFirst.Upsert(shown, new Note(62, "updated"), q => q.Id));
        Assert.Equal(new[] { new Note(63, "new"), new Note(62, "updated") }, shown);
    }
}
