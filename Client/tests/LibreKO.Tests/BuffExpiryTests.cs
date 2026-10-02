using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BuffExpiryTests
{
    [Theory]
    [InlineData(BuffExpiry.DamageOverTimeCured, 0, MagicType.DotHeal, -30, true)]
    [InlineData(BuffExpiry.DamageOverTimeCured, 0, MagicType.DotHeal, 40, false)]
    [InlineData(BuffExpiry.HealOverTimeEnded, 0, MagicType.DotHeal, 40, true)]
    [InlineData(BuffExpiry.HealOverTimeEnded, 0, MagicType.DotHeal, -30, false)]
    [InlineData(BuffExpiry.DamageOverTimeCured, 0, MagicType.Buff, -30, false)]
    [InlineData(6, 6, MagicType.Buff, 0, true)]
    [InlineData(6, 7, MagicType.Buff, 0, false)]
    [InlineData(0, 0, MagicType.Buff, 0, false)]
    public void TheExpiryCodePicksTheChipsItRemoves(int code, int buffType, int type1, int timeDamage, bool removed)
    {
        Assert.Equal(removed, BuffExpiry.Removes(code, buffType, type1, timeDamage));
    }
}
