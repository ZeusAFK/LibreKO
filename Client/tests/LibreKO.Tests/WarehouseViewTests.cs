using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WarehouseViewTests
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [100] = "Raptor", [200] = "Water of Ibexs", [300] = "Raptor(+9)",
    };

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(27, 0, 1, 3)]
    [InlineData(47, 0, 1, 23)]
    [InlineData(48, 1, 2, 0)]
    [InlineData(191, 3, 7, 23)]
    public void ScreenPagesOf48MapOntoServerPagesOf24(int abs, int uiPage, int serverPage, int serverCell)
    {
        Assert.Equal(uiPage, WarehouseView.UiPageOf(abs));
        Assert.Equal(serverPage, WarehouseView.ServerPage(abs));
        Assert.Equal(serverCell, WarehouseView.ServerCell(abs));
    }

    [Fact]
    public void SearchMarksMatchingCellsAndThePagesThatHoldThem()
    {
        var ids = new int[WarehouseView.Slots];
        ids[2] = 100;
        ids[60] = 300;
        ids[61] = 200;
        var hits = WarehouseView.Matches(ids, "raptor", id => Names[id]);
        Assert.Equal(new HashSet<int> { 2, 60 }, hits);
        Assert.Equal(new[] { true, true, false, false }, WarehouseView.PagesWithHits(hits));
    }

    [Fact]
    public void SearchByItemNumberAndEmptySearch()
    {
        var ids = new int[WarehouseView.Slots];
        ids[5] = 200;
        Assert.Equal(new HashSet<int> { 5 }, WarehouseView.Matches(ids, "200", id => Names[id]));
        Assert.Empty(WarehouseView.Matches(ids, "  ", id => Names[id]));
    }
}
