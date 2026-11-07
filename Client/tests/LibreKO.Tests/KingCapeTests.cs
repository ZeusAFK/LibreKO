using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class KingCapeTests
{
    private const int ClanCape = 12;
    private const int NoCape = 0;

    [Theory]
    [InlineData(KingCape.Karus)]
    [InlineData(KingCape.ElMorad)]
    public void AKingKeepsHisCapeWhenHisClanChangesOrHeLeavesIt(int kingCape)
    {
        Assert.Equal(kingCape, KingCape.AfterClanChange(kingCape, ClanCape));
        Assert.Equal(kingCape, KingCape.AfterClanChange(kingCape, NoCape));
    }

    [Fact]
    public void AMemberTakesTheClanCape() =>
        Assert.Equal(ClanCape, KingCape.AfterClanChange(NoCape, ClanCape));

    [Fact]
    public void AMemberWhoLeavesLosesTheClanCape() =>
        Assert.Equal(NoCape, KingCape.AfterClanChange(ClanCape, NoCape));

    [Fact]
    public void TheGmCapeIsNotAKingCape() =>
        Assert.False(KingCape.Is(GmCape.Id));
}
