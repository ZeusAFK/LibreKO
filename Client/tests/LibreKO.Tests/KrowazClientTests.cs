using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class KrowazClientTests
{
    private const float Frame = 1f / 60f;
    private const int Frames = 2000;

    [Fact]
    public void AnOpeningGateRisesTenUnitsAndStops()
    {
        var slide = new GateSlide();
        slide.Place(open: false);
        slide.SetOpen(true);

        int frames = 0;
        while (slide.Moving && frames++ < Frames)
            Assert.False(slide.Step(Frame));

        Assert.Equal(GateSlide.OpenHeight, slide.Current);
        Assert.Equal(GateSlide.SettlePhase, slide.Phase);
    }

    [Fact]
    public void AClosingGateBouncesOnceBeforeItSettles()
    {
        var slide = new GateSlide();
        slide.Place(open: true);
        slide.SetOpen(false);

        int bounces = 0, frames = 0;
        while (slide.Moving && frames++ < Frames)
            if (slide.Step(Frame)) bounces++;

        Assert.Equal(1, bounces);
        Assert.Equal(0f, slide.Current);
    }

    [Fact]
    public void AGateSeenOnArrivalIsPlacedWithoutMoving()
    {
        var slide = new GateSlide();
        slide.Place(open: true);
        Assert.False(slide.Moving);
        Assert.Equal(GateSlide.OpenHeight, slide.Current);
    }

    [Fact]
    public void TheGateTrembleStaysSmallAndEnds()
    {
        var shake = new CameraShake();
        Assert.Equal(0f, shake.Step(Frame));
        shake.Start(CameraShake.GateSeconds, CameraShake.GateAmplitude, CameraShake.GateRate);

        int frames = 0;
        while (shake.Active && frames++ < Frames)
            Assert.InRange(shake.Step(Frame), -CameraShake.GateAmplitude, CameraShake.GateAmplitude);

        Assert.False(shake.Active);
        Assert.InRange(frames * Frame, CameraShake.GateSeconds, CameraShake.GateSeconds + 1f);
    }

    [Fact]
    public void TheGasStateReadsTheServersBytes()
    {
        var p = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        p.WriteByte((byte)ObjectEventType.PoisonGas);
        p.WriteByte(1);
        p.WriteInt(10234);
        p.WriteByte(1);
        p.ResetOffset();
        p.ReadByte();
        p.ReadByte();

        Assert.Equal((10234, true), ObjectEventWire.ReadShapeState(p));
    }

    [Fact]
    public void TheKrowazNoticeCarriesItsSeconds()
    {
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte((byte)ChatType.KrowazNotice);
        p.WriteShort(30);
        p.ResetOffset();
        p.ReadByte();

        Assert.Equal(30, ChatWire.ReadKrowazSeconds(p));
    }
}
