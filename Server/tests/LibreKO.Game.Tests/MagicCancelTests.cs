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

public class MagicCancelTests : GameTestBase
{
    private const int ArcShot = 102010;
    private const int MultipleShot = 102040;
    private const int Healing = 211509;

    [Fact]
    public async Task CancellingADrawFreesTheCasterForTheNextSkill()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMagic(ArcShot).Returns(Ranged(ArcShot, castTenths: 13));
                gameData.GetMagic(MultipleShot).Returns(Ranged(MultipleShot, castTenths: 10));
            });
        var (caster, client, sent) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, ArcShot, caster, 0));
        Replies(sent).Should().Equal((MagicProcessOpcode.Casting, ArcShot));
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, ArcShot, caster, caster.CharacterId));
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, MultipleShot, caster, 0));
        Replies(sent).Should().Equal(new[] { (MagicProcessOpcode.Casting, MultipleShot) },
            "a cancelled draw must not leave the caster marked as still casting");
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, MultipleShot, caster, caster.CharacterId));
        sent.Clear();
        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, ArcShot, caster, 0));
        Replies(sent).Should().Equal(new[] { (MagicProcessOpcode.Casting, ArcShot) },
            "cancelling refunds the skill's own cooldown too");
    }

    [Fact]
    public async Task CancellingAHealMidCastAbortsItInsteadOfLandingIt()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetMagic(Healing).Returns(new MagicData
            {
                Id = Healing, Type1 = 3, Moral = 2, Range = 20, CastTime = 15, ReCastTime = 0,
            }));
        var (caster, client, sent) = CreateArcher(provider);
        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Casting, Healing, caster, caster.CharacterId));
        caster.CastingSkillId.Should().Be(Healing);
        sent.Clear();

        await coordinator.HandleAsync(client, Magic(MagicProcessOpcode.Fail, Healing, caster, caster.CharacterId));

        caster.CastingSkillId.Should().Be(0, "moving during the cast cancels the heal");
        Replies(sent).Should().BeEmpty("a cancelled heal must not execute or echo an effecting stage");
    }

    private static MagicData Ranged(int id, int castTenths) => new()
    {
        Id = id,
        Type1 = 2,
        Moral = 7,
        Range = 40,
        CastTime = (byte)castTenths,
        ReCastTime = 30,
    };

    private static (UserSession Caster, IClient Client, List<Packet> Sent) CreateArcher(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var caster = sessions.CreateSession(client, characterId: 710, accountId: 810);
        caster.Name = "Archer";
        caster.Class = 102;
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

    private static List<(MagicProcessOpcode Sub, int SkillId)> Replies(List<Packet> packets)
    {
        var list = new List<(MagicProcessOpcode, int)>();
        foreach (var p in packets)
        {
            if (p.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS) continue;
            p.ResetOffset();
            list.Add(((MagicProcessOpcode)p.ReadByte(), p.ReadInt()));
        }
        return list;
    }
}
