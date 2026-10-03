using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PagingTests
{
    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 2)]
    [InlineData(0, -1, 3, 0)]
    [InlineData(1, -1, 3, 0)]
    [InlineData(0, 1, 0, 0)]
    public void StepsStayInsideThePages(int page, int delta, int pages, int expected) =>
        Assert.Equal(expected, Paging.Step(page, delta, pages));

    [Fact]
    public void TheCaptionCountsFromOne()
    {
        Assert.Equal("Page 2 / 5", Paging.Caption(1, 5));
        Assert.Equal("Page 1 / 1", Paging.Caption(0, 0));
    }
}
