using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ClanRenameTests : GameTestBase
{
    private const int ClanNameScroll = 800086000;
    private const short ClanId = 15;
    private const byte ClanRenameRequest = 16;
    private const byte ChiefFame = 1;

    [Fact]
    public async Task TheChiefsScrollLeavesTheBagWhenTheClanIsRenamed()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Knights.Add(new KnightsEntity { Id = ClanId, Name = "Devs", Chief = "Chief", Nation = (byte)AccountNation.Karus, Flag = 1, Members = 1 });
                db.SaveChanges();
            },
            gameData => gameData.GetItem(ClanNameScroll).Returns(new ItemData
            {
                Num = ClanNameScroll, Name = "Clan Name Change Scroll", Kind = 255, Slot = 15, Duration = 1, ReqLevelMax = 100,
            }));

        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.Knights.AddClan(ClanId, new KnightsEntity { Id = ClanId, Name = "Devs", Chief = "Chief", Nation = (byte)AccountNation.Karus, Flag = 1, Members = 1 });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var chief = sessions.CreateSession(client, characterId: 500, accountId: 501);
        chief.Name = "Chief";
        chief.Level = 60;
        chief.Nation = AccountNation.Karus;
        chief.ZoneId = 21;
        chief.X = 800;
        chief.Z = 400;
        chief.KnightsId = ClanId;
        chief.KnightsFame = ChiefFame;
        chief.Inventory[InventoryConstants.SlotMax].ItemId = ClanNameScroll;
        chief.Inventory[InventoryConstants.SlotMax].Count = 1;
        chief.Inventory[InventoryConstants.SlotMax].Durability = 1;
        sessions.Regions.AddToRegion(chief);

        var packet = new Packet(GameOpcodes.GS_NAME_CHANGE);
        packet.WriteByte(ClanRenameRequest);
        packet.WriteString("Coders");
        packet.ResetOffset();
        await provider.GetRequiredService<IMiscPacketCoordinator>().HandleNameChangeAsync(client, packet);

        sessions.Knights.GetClan(ClanId)!.Name.Should().Be("Coders");
        chief.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE,
            "the client must see the scroll leave the bag");
    }
}
