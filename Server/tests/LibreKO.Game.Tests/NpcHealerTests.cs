using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class NpcHealerTests
{
    [Fact]
    public void OnlyAHealerTypeNpcHeals()
    {
        new NpcInstance { NpcType = NpcData.TypeHealer, Magic3 = 300106 }.IsHealer.Should().BeTrue();
        new NpcInstance { NpcType = 0, Magic3 = 300133 }.IsHealer.Should().BeFalse(
            "a Deruvish carries a spell id but is not a healer");
    }
}
