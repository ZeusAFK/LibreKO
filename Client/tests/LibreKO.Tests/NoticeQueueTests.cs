using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NoticeQueueTests
{
    [Fact]
    public void NoticesWaitTheirTurn()
    {
        var queue = new NoticeQueue();
        Assert.True(queue.Enqueue("war starts"));
        Assert.Equal("war starts", queue.Current);
        Assert.False(queue.Enqueue("castle siege"));
        Assert.Equal("war starts", queue.Current);
        Assert.Equal("castle siege", queue.Advance());
        Assert.Null(queue.Advance());
        Assert.Null(queue.Current);
    }

    [Fact]
    public void ARepeatedNoticeIsShownOnceAndTheQueueIsBounded()
    {
        var queue = new NoticeQueue();
        queue.Enqueue("same");
        queue.Enqueue("same");
        Assert.Null(queue.Advance());

        queue.Enqueue("first");
        for (int i = 0; i < NoticeQueue.Capacity + 3; i++) queue.Enqueue($"n{i}");
        int shown = 0;
        while (queue.Advance() != null) shown++;
        Assert.Equal(NoticeQueue.Capacity, shown);
    }
}
