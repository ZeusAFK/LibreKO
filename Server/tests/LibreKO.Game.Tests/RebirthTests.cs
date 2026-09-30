using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class RebirthTests
{
    private const int QualificationOfRebirth = 900_579_000;

    private static (CharacterDevelopmentPacketCoordinator Coordinator, UserSession Session, IClient Client, IPlayerProgressionService Progression)
        Arrange(byte level, short rebirthLevel = 0)
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessions.CreateSession(client, 1, 1);
        session.Level = level;
        session.RebirthLevel = rebirthLevel;
        var slot = session.Inventory[InventoryConstants.SlotMax];
        slot.ItemId = QualificationOfRebirth;
        slot.Count = 1;
        var progression = Substitute.For<IPlayerProgressionService>();
        var coordinator = new CharacterDevelopmentPacketCoordinator(
            sessions,
            Substitute.For<IGameDataService>(),
            Substitute.For<IJobChangeService>(),
            progression,
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ILogger<CharacterDevelopmentPacketCoordinator>>());
        return (coordinator, session, client, progression);
    }

    private static Packet RebirthRequest(byte str, byte sta, byte dex, byte intel, byte cha)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.RebirthStatChange);
        packet.WriteByte(str);
        packet.WriteByte(sta);
        packet.WriteByte(dex);
        packet.WriteByte(intel);
        packet.WriteByte(cha);
        packet.ResetOffset();
        return packet;
    }

    [Fact]
    public async Task ARebirthAtLevel83ConsumesTheQualificationAddsTwoPointsAndCompletes()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(1, 0, 1, 0, 0));

        session.RebirthLevel.Should().Be(1);
        session.RebStr.Should().Be(1);
        session.RebDex.Should().Be(1);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(0, "the Qualification of Rebirth is spent");
        await progression.Received(1).CompleteRebirthAsync(session);
    }

    [Theory]
    [InlineData(82)]
    [InlineData(60)]
    public async Task ARebirthBelowLevel83IsRefusedAndKeepsTheQualification(byte level)
    {
        var (coordinator, session, client, progression) = Arrange(level);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(0);
        session.RebStr.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0)]
    [InlineData(2, 1, 0, 0, 0)]
    public async Task ARebirthMustPlaceExactlyTwoPoints(byte str, byte sta, byte dex, byte intel, byte cha)
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(str, sta, dex, intel, cha));

        session.RebirthLevel.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
    }

    [Fact]
    public async Task TheFifteenthRebirthIsTheLast()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel, RebirthBonus.MaxRebirthLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(0, 2, 0, 0, 0));

        session.RebirthLevel.Should().Be(RebirthBonus.MaxRebirthLevel);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
    }
}
