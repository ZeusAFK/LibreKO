using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class RentalNpcTests : GameTestBase
{
    private const short Soren = 14403;
    private const int RentalGroup = 231000;
    private const byte RentalNpcSub = 3;
    private const short Unavailable = -1;
    private const byte Moradon = 21;

    [Fact]
    public async Task ARentalNpcSaysTheServiceIsUnavailable()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetNpc(Soren, Arg.Any<bool>()).Returns(new NpcData
            {
                Id = Soren, Name = "[Novice Weapon Rental] Soren", NpcType = NpcData.TypeRental, SellingGroup = RentalGroup, IsMonster = false,
            }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: 920, accountId: 930);
        session.Name = "Renter";
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.Level = 20;
        session.Nation = AccountNation.Karus;
        session.X = 800;
        session.Z = 400;
        sessions.Regions.AddToRegion(session);
        var npc = sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = Soren, Name = "[Novice Weapon Rental] Soren", NpcType = NpcData.TypeRental, ZoneId = Moradon,
            X = 800, Z = 400, SpawnX = 800, SpawnZ = 400, Hp = 100, MaxHp = 100,
        });

        var click = new Packet(GameOpcodes.GS_NPC_EVENT);
        click.WriteByte(1);
        click.WriteInt(npc.UniqueId);
        click.ResetOffset();
        await provider.GetRequiredService<IQuestNpcInteractionService>().HandleNpcEventAsync(client, click);

        var reply = sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_RENTAL).Subject;
        reply.ResetOffset();
        reply.ReadByte().Should().Be(RentalNpcSub);
        reply.ReadShort().Should().Be(Unavailable);
        reply.ReadInt().Should().Be(RentalGroup);
    }
}
