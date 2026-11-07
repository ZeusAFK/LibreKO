using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class ScriptCastTests
{
    private const int SpiritOfSpecialist = 504005;
    private const int SpiritOfGrandmaster = 504006;
    private const int ShoutOfLogos = 504007;
    private const int NoSuchSkill = 25178;
    private const int EnchanterId = 4242;
    private const short OneHour = 3600;

    private sealed record Fixture(
        UserSession Session, QuestScriptContext Context, SessionManager Sessions,
        IUserNotificationService Notifications, List<Packet> Sent);

    private static Fixture World()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        var enchanter = new NpcInstance { UniqueId = EnchanterId, NpcId = 25178, ZoneId = session.ZoneId };

        var gameData = Substitute.For<IGameDataService>();
        gameData.GetMagic(Arg.Any<int>()).Returns(call => call.Arg<int>() == NoSuchSkill
            ? null
            : new MagicData { Id = call.Arg<int>(), Type1 = call.Arg<int>() == ShoutOfLogos ? (byte)0 : (byte)MagicSkillType.Buff });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [SpiritOfSpecialist] = ExperienceBuff(SpiritOfSpecialist, 150),
            [SpiritOfGrandmaster] = ExperienceBuff(SpiritOfGrandmaster, 200),
        });

        var context = new QuestScriptContext(session, enchanter, gameData, sessions, Substitute.For<ILogger>(), 1);
        return new Fixture(session, context, sessions, Substitute.For<IUserNotificationService>(), sent);
    }

    private static MagicType4Data ExperienceBuff(int id, short percent) => new()
    {
        Id = id, BuffType = (byte)BuffType.Experience, Duration = OneHour, ExpPct = percent,
        Speed = 100, AttackSpeed = 100, AcPct = 100, Attack = 100, MagicAttack = 100,
        MaxHPPct = 100, MaxMPPct = 100, HitRate = 100, AvoidRate = 100,
    };

    private static Task ApplyAsync(Fixture fixture)
    {
        var provider = new ServiceCollection()
            .AddSingleton(fixture.Sessions)
            .AddSingleton(fixture.Notifications)
            .BuildServiceProvider();
        return new ScriptEffectApplier(
                Substitute.For<IGameDataService>(), Substitute.For<ICharacterStatePersister>(), provider,
                Substitute.For<ILogger<ScriptEffectApplier>>())
            .ApplyAsync(fixture.Session, fixture.Context, "25178.quest");
    }

    [Fact]
    public void AScriptedBuffCarriesItsBuffTypeAndExperienceBonus()
    {
        var fixture = World();

        fixture.Context.Character.CastSkill(0, SpiritOfSpecialist).Should().BeTrue();

        var buff = fixture.Session.ActiveBuffs[SpiritOfSpecialist];
        buff.BuffType.Should().Be(BuffType.Experience);
        buff.SpecialAmount.Should().Be(150);
    }

    [Fact]
    public async Task AScriptedBuffIsShownAtOnceAsCastByTheNpc()
    {
        var fixture = World();
        fixture.Context.Character.CastSkill(0, SpiritOfSpecialist);

        await ApplyAsync(fixture);

        await fixture.Notifications.Received(1).SendStatUpdateAsync(fixture.Session);
        var packet = fixture.Sent.Single(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS);
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)MagicProcessOpcode.Effecting);
        packet.ReadInt().Should().Be(SpiritOfSpecialist);
        packet.ReadInt().Should().Be(EnchanterId);
        packet.ReadInt().Should().Be(fixture.Session.CharacterId);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(MagicProcessPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(OneHour);
    }

    [Fact]
    public void AnotherBuffOfTheSameTypeRefusesTheCast()
    {
        var fixture = World();
        fixture.Context.Character.CastSkill(0, SpiritOfGrandmaster);

        fixture.Context.Character.CastSkill(0, SpiritOfSpecialist).Should().BeFalse();
        fixture.Session.ActiveBuffs.Should().NotContainKey(SpiritOfSpecialist);
    }

    [Fact]
    public void CastingTheSameBuffAgainRefreshesIt()
    {
        var fixture = World();
        fixture.Context.Character.CastSkill(0, SpiritOfSpecialist);

        fixture.Context.Character.CastSkill(0, SpiritOfSpecialist).Should().BeTrue();
    }

    [Fact]
    public void ASkillWithNoBuffRowSucceedsWithoutABuff()
    {
        var fixture = World();

        fixture.Context.Character.CastSkill(0, ShoutOfLogos).Should().BeTrue();
        fixture.Session.ActiveBuffs.Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownSkillFails()
    {
        World().Context.Character.CastSkill(0, NoSuchSkill).Should().BeFalse();
    }
}
