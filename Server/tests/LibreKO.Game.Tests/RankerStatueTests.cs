using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibreKO.Game.Tests;

public class RankerStatueTests : GameTestBase
{
    private const byte Moradon = 21;
    private const int PlateArmor = 206001000;
    private const int Sword = 110010000;

    [Fact]
    public async Task EachNationsTopMonthlyPlayersStandOnTheirStatuesInOrder()
    {
        using var provider = CreateProvider(db =>
        {
            db.Accounts.AddRange(
                new Account { Id = 1, Login = "a", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 2, Login = "b", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 3, Login = "c", Password = "x", Nation = AccountNation.ElMorad });
            db.Characters.AddRange(
                new Character
                {
                    AccountId = 1, Name = "Veteran", Race = 2, Class = 106, Face = 3, Hair = 4,
                    LoyaltyMonthly = 900, Items = ItemsWith((InventoryConstants.Breast, PlateArmor), (InventoryConstants.RightHand, Sword)),
                },
                new Character { AccountId = 2, Name = "Rising", Race = 1, Class = 101, LoyaltyMonthly = 500 },
                new Character { AccountId = 3, Name = "Aurelia", Race = 12, Class = 201, LoyaltyMonthly = 50 });
        });
        var sessions = provider.GetRequiredService<SessionManager>();
        var karusFirst = Statue(NpcData.TypeRankerKarusFirst, 790);
        var karusSecond = Statue(NpcData.TypeRankerKarusFirst + 1, 782);
        var karusThird = Statue(NpcData.TypeRankerKarusFirst + 2, 774);
        var elMoradFirst = Statue(NpcData.TypeRankerElMoradFirst, 842);
        foreach (var statue in new[] { karusFirst, karusSecond, karusThird, elMoradFirst })
            sessions.Regions.SpawnNpc(statue);

        await provider.GetRequiredService<INationRankService>().RefreshAsync();

        karusFirst.StatueLook.Should().Be(new NpcSpawnPacketWriter.StatueLook(
            "Veteran", 2, 106, 3, 4, 0, PlateArmor, 0, 0, 0, Sword, 0));
        karusSecond.StatueLook?.Name.Should().Be("Rising");
        karusThird.StatueLook.Should().BeNull();
        elMoradFirst.StatueLook?.Name.Should().Be("Aurelia");
    }

    [Fact]
    public void AStatueRecordEndsWithItsRankersLookAndAnEmptyStatueWithAnEmptyName()
    {
        var look = new NpcSpawnPacketWriter.StatueLook("Veteran", 2, 106, 3, 4, 1, 2, 3, 4, 5, 6, 7);

        var filled = TailOf(NpcData.TypeRankerKarusFirst, look);
        filled.ReadString().Should().Be("Veteran");
        filled.ReadByte().Should().Be(2);
        filled.ReadShort().Should().Be(106);
        filled.ReadByte().Should().Be(3);
        filled.ReadInt().Should().Be(4);
        Enumerable.Range(0, 7).Select(_ => filled.ReadInt()).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        filled.RemainingBytes.Should().Be(0);

        var empty = TailOf(NpcData.TypeRankerElMoradFirst + 2, null);
        empty.ReadShort().Should().Be(0);
        empty.RemainingBytes.Should().Be(0);

        TailOf(NpcData.TypeTalk, look).RemainingBytes.Should().Be(0);
    }

    private static NpcInstance Statue(int type, int x) => new()
    {
        NpcId = (short)(32082 + type - NpcData.TypeRankerKarusFirst), NpcType = (byte)type,
        ZoneId = Moradon, X = x, Z = 561,
    };

    private static Packet TailOf(int type, NpcSpawnPacketWriter.StatueLook? look)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        NpcSpawnPacketWriter.WriteRecord(packet, new NpcSpawnPacketWriter.NpcState(
            1, 32082, false, 30090, 0, (byte)type, 100, 0, 0, 1, 1, 7900, 5610, 470, 0, 0, 90, Statue: look));
        packet.ResetOffset();
        packet.ReadBytes(2 + 1 + 2 + 4 + 1 + 4 + 2 + 4 + 4 + 1 + 1 + 2 + 2 + 2 + 4 + 1 + 2 + 2 + 1 + 1);
        return packet;
    }

    private static byte[] ItemsWith(params (int Slot, int ItemId)[] items)
    {
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var i = 0; i < inventory.Length; i++)
            inventory[i] = new ItemSlot();
        foreach (var (slot, itemId) in items)
            inventory[slot].ItemId = itemId;
        return UserSessionBinaryState.SerializeItems(inventory);
    }
}
