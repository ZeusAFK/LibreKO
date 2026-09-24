using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GuardSummonTests : GameTestBase
{
    private const byte Zone = 21;
    private const int GuardStrength = 500;
    private const int SturdyMonsterHp = 100_000;
    private const int Ticks = 20;
    private const short NationRecordOffset = 24;

    [Fact]
    public void TheGuardIsAnnouncedWithItsOwnersNationSoThatSideSeesItAsFriendly()
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        NpcSpawnPacketWriter.WriteRecord(packet, new NpcSpawnPacketWriter.NpcState(
            1, 8850, true, 6200, 0, NpcData.TypeGuardSummon, 100, 0, 0,
            (byte)AccountNation.ElMorad, 83, 100, 100, 0, false, 0, 0));

        packet.ResetOffset();
        for (var i = 0; i < NationRecordOffset; i++)
            packet.ReadByte();
        packet.ReadByte().Should().Be((byte)AccountNation.ElMorad, "a summoned guard keeps its nation where monsters send none");
    }

    [Fact]
    public async Task TheGuardStrikesAMonsterBesideItAndCreditsItsOwner()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var owner = Player(sessions, 700, AccountNation.Karus);
        var guard = Guard(sessions, owner);
        var monster = sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 101, IsMonster = true, ZoneId = Zone, X = 102, Z = 100,
            Hp = SturdyMonsterHp, MaxHp = SturdyMonsterHp,
        });

        await Tick(provider, guard);

        monster.Hp.Should().BeLessThan(SturdyMonsterHp);
        monster.WasDamagedBy(owner.CharacterId).Should().BeTrue("the guard fights for its owner");
    }

    [Fact]
    public async Task TheGuardLeavesItsOwnSideAlone()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var owner = Player(sessions, 700, AccountNation.Karus);
        var friend = Player(sessions, 701, AccountNation.Karus);
        var guard = Guard(sessions, owner);

        await Tick(provider, guard);

        friend.Hp.Should().Be(friend.MaxHp);
        owner.Hp.Should().Be(owner.MaxHp);
        NpcHostility.IsAttackableBy(guard, owner).Should().BeFalse("the owner cannot strike its own guard");
        guard.IsAttackable.Should().BeFalse();
    }

    private static NpcInstance Guard(SessionManager sessions, UserSession owner) =>
        sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 8850, IsMonster = true, NpcType = NpcData.TypeGuardSummon,
            Nation = (EntityNation)owner.Nation, OwnerCharId = owner.CharacterId,
            ZoneId = Zone, X = 100, Z = 100, Hp = 30000, MaxHp = 30000,
            Attack1 = GuardStrength, HitRate = 30000, AttackDelay = 1, AttackRange = 25,
        });

    private static async Task Tick(ServiceProvider provider, NpcInstance guard)
    {
        var ai = provider.GetRequiredService<IGuardSummonAiService>();
        var now = DateTime.UtcNow.Ticks;
        for (var i = 0; i < Ticks; i++)
            await ai.TickAsync(guard, now + i * TimeSpan.TicksPerSecond);
    }

    private static UserSession Player(SessionManager sessions, int characterId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var player = sessions.CreateSession(client, characterId, characterId + 100);
        player.Name = $"P{characterId}";
        player.Class = 203;
        player.Level = 60;
        player.Nation = nation;
        player.ZoneId = Zone;
        player.X = 101;
        player.Z = 101;
        player.Hp = 1000;
        player.MaxHp = 1000;
        sessions.Regions.AddToRegion(player);
        return player;
    }
}
