using Godot;
using Xunit;

namespace LibreKO.Tests;

public class DiscBatchTests
{
    [Fact]
    public void EachDiscIsTheEngineCircleFanAroundItsCentre()
    {
        var batch = new DiscBatch();
        var centre = new Vector2(10f, 20f);

        batch.Add(centre, 5f, Colors.Red);

        Assert.Equal(1, batch.Count);
        Assert.Equal(DiscBatch.Segments + 2, batch.Points.Length);
        Assert.Equal(centre, batch.Points[DiscBatch.Segments + 1]);
        Assert.Equal(new Vector2(15f, 20f), batch.Points[0]);
        Assert.Equal(15f, batch.Points[DiscBatch.Segments].X);
        Assert.Equal(20f, batch.Points[DiscBatch.Segments].Y, 4);
        Assert.Equal(25f, batch.Points[DiscBatch.Segments / 4].Y, 4);
        for (int i = 0; i <= DiscBatch.Segments; i++)
            Assert.Equal(5f, batch.Points[i].DistanceTo(centre), 4);
        Assert.All(batch.Colors.ToArray(), c => Assert.Equal(Colors.Red, c));
    }

    [Fact]
    public void TrianglesRunFromTheCentreAcrossEachRimStepInOrder()
    {
        var batch = new DiscBatch();
        batch.Add(Vector2.Zero, 1f, Colors.White);
        batch.Add(Vector2.One, 2f, Colors.Black);

        var indices = batch.Indices.ToArray();

        Assert.Equal(2 * DiscBatch.IndicesPerDisc, indices.Length);
        Assert.Equal(new[] { 65, 0, 1 }, indices[..3]);
        Assert.Equal(new[] { 65, 63, 64 }, indices[(DiscBatch.IndicesPerDisc - 3)..DiscBatch.IndicesPerDisc]);
        int second = DiscBatch.IndicesPerDisc;
        Assert.Equal(new[] { 66 + 65, 66, 67 }, indices[second..(second + 3)]);
        Assert.Equal(Colors.Black, batch.Colors[DiscBatch.VerticesPerDisc]);
    }

    [Fact]
    public void GrowingKeepsEarlierDiscsAndClearingStartsOver()
    {
        var batch = new DiscBatch();
        for (int i = 0; i < 100; i++) batch.Add(new Vector2(i, 0f), 1f, new Color(i / 100f, 0f, 0f));

        Assert.Equal(100, batch.Count);
        Assert.Equal(new Vector2(3f, 0f), batch.Points[3 * DiscBatch.VerticesPerDisc + DiscBatch.Segments + 1]);
        Assert.Equal(99 * DiscBatch.VerticesPerDisc + DiscBatch.Segments + 1, batch.Indices[^DiscBatch.IndicesPerDisc]);

        batch.Clear();

        Assert.Equal(0, batch.Count);
        Assert.Equal(0, batch.Points.Length);
        batch.Add(new Vector2(7f, 7f), 1f, Colors.Blue);
        Assert.Equal(new Vector2(7f, 7f), batch.Points[DiscBatch.Segments + 1]);
    }
}
