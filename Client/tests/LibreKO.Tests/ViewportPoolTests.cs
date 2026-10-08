using Xunit;

namespace LibreKO.Tests;

public class ViewportPoolTests
{
    private const ulong WorldViewport = 1;
    private const ulong PreviewViewport = 2;
    private static readonly FxPartKey Glow = new("glow", 0, 1f, true);

    [Fact]
    public void AnotherViewportNeverSeesTheEmittersOfTheWorld()
    {
        var pool = new ViewportPool<FxPartKey, object>();
        var worldEmitter = new object();
        pool.Add(WorldViewport, Glow, worldEmitter);

        Assert.Empty(pool.Shelf(PreviewViewport, Glow));
        Assert.Same(worldEmitter, Assert.Single(pool.Shelf(WorldViewport, Glow)));
    }

    [Fact]
    public void AForgottenEmitterLeavesItsShelfAndTheLiveCount()
    {
        var pool = new ViewportPool<FxPartKey, object>();
        var worldEmitter = new object();
        var previewEmitter = new object();
        pool.Add(WorldViewport, Glow, worldEmitter);
        pool.Add(PreviewViewport, Glow, previewEmitter);

        Assert.True(pool.Forget(PreviewViewport, Glow, previewEmitter));

        Assert.Equal(1, pool.Live);
        Assert.Empty(pool.Shelf(PreviewViewport, Glow));
        Assert.Same(worldEmitter, Assert.Single(pool.Items));
    }

    [Fact]
    public void ForgettingTwiceOrInTheWrongViewportChangesNothing()
    {
        var pool = new ViewportPool<FxPartKey, object>();
        var emitter = new object();
        pool.Add(WorldViewport, Glow, emitter);

        Assert.False(pool.Forget(PreviewViewport, Glow, emitter));
        Assert.True(pool.Forget(WorldViewport, Glow, emitter));
        Assert.False(pool.Forget(WorldViewport, Glow, emitter));

        Assert.Equal(0, pool.Live);
    }

    [Fact]
    public void ClearingEmptiesEveryViewport()
    {
        var pool = new ViewportPool<FxPartKey, object>();
        pool.Add(WorldViewport, Glow, new object());
        pool.Add(PreviewViewport, Glow, new object());

        pool.Clear();

        Assert.Equal(0, pool.Live);
        Assert.Empty(pool.Items);
    }
}
