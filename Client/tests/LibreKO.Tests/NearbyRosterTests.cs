using System.Collections.Generic;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NearbyRosterTests
{
    private static readonly NearbyViewer Me = new("Zeus", Nation: 2, ClanId: 7, X: 100, Z: 100, Gm: false);

    [Theory]
    [InlineData(Net.NearbyPlayersSignSub, Net.NearbyPlayersRefreshSub, 0, true)]
    [InlineData(Net.NearbyPlayersRefreshSub, Net.NearbyPlayersRefreshSub, 0, false)]
    [InlineData(Net.NearbyPlayersSignSub, Net.NearbyPlayersSignSub, 0, false)]
    [InlineData(Net.NearbyPlayersSignSub, Net.NearbyPlayersRefreshSub, 3, false)]
    public void AnEmptyListUnderAnotherSubIsOnlyTheHeader(byte sub, byte requested, int count, bool header) =>
        Assert.Equal(header, Net.IsNearbyListHeader(sub, requested, count));

    [Fact]
    public void SightAndServerListMergeSortedByDistanceWithoutMe()
    {
        var listed = new List<NearbyListed>
        {
            new("Far", 1, 400, 100, 0),
            new("Near", 2, 110, 100, 0),
            new("Zeus", 2, 100, 100, 7),
        };
        var seen = new List<NearbySeen> { new(5, "Near", 2, 60, 105, 0, 110, 100, Gm: false) };

        var rows = NearbyRoster.Build(Me, listed, seen, new HashSet<string>());

        Assert.Equal(new[] { "Near", "Far" }, new[] { rows[0].Name, rows[1].Name });
        Assert.Equal(5, rows[0].Id);
        Assert.Equal(60, rows[0].Level);
        Assert.Equal(-1, rows[1].Id);
        Assert.Equal(10f, rows[0].Distance, 3);
    }

    [Fact]
    public void RelationsGoPartyThenClanThenNation()
    {
        var seen = new List<NearbySeen>
        {
            new(1, "Mate", 2, 50, 101, 9, 101, 100, false),
            new(2, "Clanmate", 2, 50, 101, 7, 102, 100, false),
            new(3, "Countryman", 2, 50, 101, 0, 103, 100, false),
            new(4, "Foe", 1, 50, 101, 0, 104, 100, false),
        };
        var rows = NearbyRoster.Build(Me, new List<NearbyListed>(), seen, new HashSet<string> { "mate" });
        Assert.Equal(new[] { NearbyRelation.Party, NearbyRelation.Clan, NearbyRelation.Ally, NearbyRelation.Enemy },
            new[] { rows[0].Relation, rows[1].Relation, rows[2].Relation, rows[3].Relation });
    }

    [Fact]
    public void GameMastersAreListedOnlyForGameMasters()
    {
        var seen = new List<NearbySeen> { new(1, "[GM]Op", 2, 80, 101, 0, 101, 100, Gm: true) };
        Assert.Empty(NearbyRoster.Build(Me, new List<NearbyListed>(), seen, new HashSet<string>()));
        Assert.Single(NearbyRoster.Build(Me with { Gm = true }, new List<NearbyListed>(), seen, new HashSet<string>()));
    }

    [Fact]
    public void SmallMovesDoNotCountAsAChange()
    {
        var a = new List<NearbyRow> { new("Foe", 4, 1, 50, 101, 0, 40.1f, 1, 1, NearbyRelation.Enemy) };
        var b = new List<NearbyRow> { a[0] with { Distance = 40.4f } };
        var c = new List<NearbyRow> { a[0] with { Distance = 42f } };
        Assert.True(NearbyRoster.SameRows(a, b));
        Assert.False(NearbyRoster.SameRows(a, c));
    }
}
