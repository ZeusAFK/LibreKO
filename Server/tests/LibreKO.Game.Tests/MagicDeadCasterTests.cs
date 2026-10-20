using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MagicDeadCasterTests : GameTestBase
{
    private const int FireBlast = 110518;
    private const int LifeCrystal = 480013;
    private const int LifeCrystalItem = 800159000;
    private const int SelfRevive = 480002;
    private const int SelfReviveItem = 910022000;
    private const int FriendRevive = 480001;
    private const int FriendReviveItem = 379006000;
    private const int UnofferedRevive = 480004;
    private const int UnofferedReviveItem = 800039000;
    private const byte Ronark = 71;
    private const byte SixthLevel = 6;
    private const byte FifthLevel = 5;

    [Fact]
    public async Task ADeadPlayerCannotCastAtAMonster()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(FireBlast).Returns(new MagicData
                {
                    Id = FireBlast, Type1 = 3, Moral = 7, Range = 20, CastTime = 2, ReCastTime = 50,
                });
                gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
                {
                    [FireBlast] = new()
                    {
                        Id = FireBlast, DirectType = 1, FirstDamage = -120, TimeDamage = -60, Duration = 6, Attribute = 1,
                    },
                });
            });
        var (caster, client, sent) = CreateCaster(provider);
        caster.Hp = 0;
        var worm = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = 21, X = 104, Z = 100, SpawnX = 104,
            SpawnZ = 100, Hp = 5000, MaxHp = 5000, Ac = 5, EvadeRate = 1,
        });
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, FireBlast, caster, worm.UniqueId));
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Effecting, FireBlast, caster, worm.UniqueId));

        worm.Hp.Should().Be(5000);
        worm.ActiveOverTimeEffects.Should().BeEmpty();
        Stages(sent).Should().NotContain(MagicProcessOpcode.Effecting).And.NotContain(MagicProcessOpcode.Casting);
    }

    [Theory]
    [InlineData(LifeCrystal, LifeCrystalItem, (byte)ZoneId.UnderCastle, SixthLevel, true)]
    [InlineData(LifeCrystal, LifeCrystalItem, Ronark, SixthLevel, false)]
    [InlineData(SelfRevive, SelfReviveItem, Ronark, SixthLevel, true)]
    [InlineData(SelfRevive, SelfReviveItem, (byte)ZoneId.UnderCastle, SixthLevel, false)]
    [InlineData(SelfRevive, SelfReviveItem, Ronark, FifthLevel, false)]
    [InlineData(FriendRevive, FriendReviveItem, Ronark, SixthLevel, false)]
    [InlineData(UnofferedRevive, UnofferedReviveItem, Ronark, SixthLevel, false)]
    public async Task ADeadPlayerRevivesOnlyWithWhatTheDeathWindowOffers(int skillId, int item, byte zone, byte level, bool revived)
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                foreach (var (row, reagent, moral, kind) in new[]
                         {
                             (LifeCrystal, LifeCrystalItem, SkillMoral.Self, SpecialMagicType.LifeCrystal),
                             (SelfRevive, SelfReviveItem, SkillMoral.Self, SpecialMagicType.ResurrectionSelf),
                             (FriendRevive, FriendReviveItem, SkillMoral.CorpseFriend, SpecialMagicType.ResurrectionSelf),
                             (UnofferedRevive, UnofferedReviveItem, SkillMoral.Self, SpecialMagicType.ResurrectionSelf),
                         })
                    gameData.GetMagic(row).Returns(new MagicData { Id = row, Type1 = 5, Moral = (byte)moral, UseItem = reagent });
                gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
                {
                    [LifeCrystal] = new() { Id = LifeCrystal, Type = (byte)SpecialMagicType.LifeCrystal },
                    [SelfRevive] = new() { Id = SelfRevive, Type = (byte)SpecialMagicType.ResurrectionSelf, ExpRecover = 50 },
                    [FriendRevive] = new() { Id = FriendRevive, Type = (byte)SpecialMagicType.ResurrectionSelf, ExpRecover = 60 },
                    [UnofferedRevive] = new() { Id = UnofferedRevive, Type = (byte)SpecialMagicType.ResurrectionSelf, ExpRecover = 50 },
                });
            });
        var (caster, client, _) = CreateCaster(provider);
        HoldReagent(provider, caster, item);
        caster.ZoneId = zone;
        caster.Level = level;
        caster.Hp = 0;

        await provider.GetRequiredService<IMagicPacketCoordinator>()
            .HandleAsync(client, Magic(MagicProcessOpcode.Effecting, skillId, caster, caster.CharacterId));

        (caster.Hp > 0).Should().Be(revived);
    }

    private static (UserSession Caster, IClient Client, List<Packet> Sent) CreateCaster(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = sessions.CreateSession(client, characterId: 730, accountId: 830);
        caster.Name = "Mage";
        caster.Class = 110;
        caster.Level = 60;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = 21;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = 500;
        caster.MaxHp = 500;
        caster.Mp = 500;
        caster.MaxMp = 500;
        sessions.Regions.AddToRegion(caster);
        return (caster, client, sent);
    }

    private static Packet Magic(MagicProcessOpcode sub, int skillId, UserSession caster, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)sub);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        return packet;
    }

    private static List<MagicProcessOpcode> Stages(List<Packet> packets) =>
        packets.Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .Select(p =>
            {
                p.ResetOffset();
                return (MagicProcessOpcode)p.ReadByte();
            })
            .ToList();
}
