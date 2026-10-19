using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

// A quest's gold reward stops at the coin cap instead of overflowing the purse.
public class QuestGoldRewardTests
{
    [Fact]
    public void AQuestExchangeWhoseGoldWouldPassTheCoinCapIsRefusedWhole()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        session.Money = ExchangePacketConstants.CoinMax - 100;
        var context = new QuestScriptContext(session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);

        var applied = context.Items.ApplyScriptReward([], [(InventoryConstants.ItemGold, 1_000, 0)]);

        applied.Should().BeFalse("the reward would be cut at the cap, so it is not given at all");
        session.Money.Should().Be(ExchangePacketConstants.CoinMax - 100);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(int.MaxValue)]
    public void AQuestGoldRewardStopsAtTheCoinCap(int reward)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        session.Money = ExchangePacketConstants.CoinMax - 100;
        var context = new QuestScriptContext(session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);

        context.Items.GoldGain(0, reward);

        session.Money.Should().Be(ExchangePacketConstants.CoinMax);
    }
}
