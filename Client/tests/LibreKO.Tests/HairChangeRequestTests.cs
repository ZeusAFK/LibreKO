using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class HairChangeRequestTests
{
    private const int Face = 4;
    private const int Hair = 0x03_5A_38_20;
    private const int OtherFace = 2;
    private const int OtherHair = 0x01_10_20_30;

    [Fact]
    public void ASecondRequestWaitsForTheFirstAnswer()
    {
        var request = new HairChangeRequest();

        Assert.True(request.TryBegin(Face, Hair));
        Assert.False(request.TryBegin(OtherFace, OtherHair));

        Assert.True(request.Pending);
        Assert.Equal(Face, request.Face);
        Assert.Equal(Hair, request.Hair);
    }

    [Fact]
    public void OnlyAPendingRequestIsAnswered()
    {
        var request = new HairChangeRequest();

        Assert.False(request.TryFinish());
        request.TryBegin(Face, Hair);
        Assert.True(request.TryFinish());
        Assert.False(request.TryFinish());
        Assert.False(request.Pending);
    }

    [Fact]
    public void AnAnsweredRequestFreesTheNextOne()
    {
        var request = new HairChangeRequest();
        request.TryBegin(Face, Hair);
        request.TryFinish();

        Assert.True(request.TryBegin(OtherFace, OtherHair));
        Assert.Equal(OtherFace, request.Face);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(byte.MaxValue + 1)]
    public void AFaceOutsideTheWireByteIsNotSent(int face)
    {
        var request = new HairChangeRequest();

        Assert.False(request.TryBegin(face, Hair));
        Assert.False(request.Pending);
    }
}
