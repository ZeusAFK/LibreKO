using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ExpSealTests
{
    private static Packet Bytes(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_EXP_SEAL);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case long l: p.WriteLong(l); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheRepliesAreReadInThe2619Shapes()
    {
        Assert.Equal(new ExpSealReply(ExpSealSubOpcode.On, ExpSealResult.Succeeded, 0), ExpSealReply.Read(Bytes((byte)ExpSealSubOpcode.On, (byte)1)));
        Assert.Equal(new ExpSealReply(ExpSealSubOpcode.Store, ExpSealResult.Succeeded, 100_000_000), ExpSealReply.Read(Bytes((byte)ExpSealSubOpcode.Store, (short)1, 100_000_000L)));
        Assert.Equal(new ExpSealReply(ExpSealSubOpcode.Store, (ExpSealResult)2, 0), ExpSealReply.Read(Bytes((byte)ExpSealSubOpcode.Store, (short)2)));
        Assert.Equal(new ExpSealReply(ExpSealSubOpcode.Total, ExpSealResult.Succeeded, 1_500), ExpSealReply.Read(Bytes((byte)ExpSealSubOpcode.Total, 1_500L)));
        Assert.Equal(new ExpSealReply(ExpSealSubOpcode.UseJar, (ExpSealResult)9, 0), ExpSealReply.Read(Bytes((byte)ExpSealSubOpcode.UseJar, (short)9)));
    }

    [Fact]
    public void RefusalsUseThe2619Texts()
    {
        Assert.Equal(ExpSeal.TooLittleText, ExpSeal.StoreRefusal(ExpSealResult.TooLittle));
        Assert.Equal(ExpSeal.WhileOnText, ExpSeal.StoreRefusal(ExpSealResult.WhileOn));
        Assert.Null(ExpSeal.StoreRefusal(ExpSealResult.Succeeded));
        Assert.Equal((ExpSeal.RebirthLimitText, false), ExpSeal.UseRefusal(ExpSealResult.RebirthLimit));
        Assert.Equal((ExpSeal.JarFailedText, true), ExpSeal.UseRefusal((ExpSealResult)4));
        Assert.Null(ExpSeal.UseRefusal(ExpSealResult.Succeeded));
    }

    [Fact]
    public void TheLocalChecksComeInThe2619Order()
    {
        Assert.Equal(ExpSeal.NoJarText, ExpSeal.LocalRefusal(false, 0, 0));
        Assert.Equal(ExpSeal.TooLittleText, ExpSeal.LocalRefusal(true, 99_999_999, 100_000_000));
        Assert.Equal(ExpSeal.NoAmountText, ExpSeal.LocalRefusal(true, 200_000_000, 0));
        Assert.Null(ExpSeal.LocalRefusal(true, 200_000_000, 100_000_000));
    }

    [Fact]
    public void TheSealedBarIsTheTotalOverOneBillion() => Assert.Equal(25.0, ExpSeal.Percent(250_000_000));
}
