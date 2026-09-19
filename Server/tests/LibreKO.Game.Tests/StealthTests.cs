using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class StealthTests : GameTestBase
{
    private const int Hide = 107700;
    private const int Stealth = 107645;
    private const int CatsEyes = 107715;
    private const int LupineEyes = 107735;
    private const int Inferno = 107646;

    private const byte Moradon = 21;
    private const short SightRadius = 25;

    [Fact]
    public async Task HideEndsTheMomentYouTakeAStep()
    {
        using var provider = CreateStealthProvider();
        var (session, _) = CreateRogue(provider, 700);

        await CastAsync(provider, session, Hide);
        session.Invisibility.Should().Be(InvisibilityType.DispelOnMove,
            "the skill's own description is 'invisible without moving'");

        await MoveAsync(provider, session, x: 200, z: 200);

        session.Invisibility.Should().Be(InvisibilityType.None);
        session.ActiveBuffs.Should().NotContainKey(Hide);
    }

    [Fact]
    public async Task StealthSurvivesWalkingAndEndsOnASwing()
    {
        using var provider = CreateStealthProvider();
        var (session, client) = CreateRogue(provider, 701);

        await CastAsync(provider, session, Stealth);
        session.Invisibility.Should().Be(InvisibilityType.DispelOnAttack,
            "the skill's own description is 'invisible without attacking'");

        await MoveAsync(provider, session, x: 200, z: 200);
        session.Invisibility.Should().Be(InvisibilityType.DispelOnAttack,
            "walking is not attacking");

        await SwingAsync(provider, client);

        session.Invisibility.Should().Be(InvisibilityType.None);
        session.ActiveBuffs.Should().NotContainKey(Stealth);
    }

    [Fact]
    public async Task CastingAnOffensiveSkillGivesTheRogueAway()
    {
        using var provider = CreateStealthProvider();
        var (session, _) = CreateRogue(provider, 702);

        await CastAsync(provider, session, Stealth);
        await CastAsync(provider, session, Inferno);

        session.Invisibility.Should().Be(InvisibilityType.None);
    }

    [Fact]
    public async Task GoingInvisibleTellsEveryoneNearby()
    {
        using var provider = CreateStealthProvider();
        var (session, client) = CreateRogue(provider, 703);
        var sent = Capture(client);

        await CastAsync(provider, session, Hide);

        StealthStatesIn(sent).Should().ContainSingle()
            .Which.Should().Be((byte)InvisibilityType.DispelOnMove);
    }

    [Fact]
    public async Task CatsEyesGivesTheCasterAloneTheSightRadius()
    {
        using var provider = CreateStealthProvider();
        var (caster, casterClient) = CreateRogue(provider, 704);
        var (mate, mateClient) = CreateRogue(provider, 705);
        PartyUp(provider, caster, mate);

        var casterSent = Capture(casterClient);
        var mateSent = Capture(mateClient);

        await CastAsync(provider, caster, CatsEyes);

        SightRadiiIn(casterSent).Should().Equal(SightRadius);
        SightRadiiIn(mateSent).Should().BeEmpty("only the caster sees the hidden");
    }

    [Fact]
    public async Task LupineEyesGivesEveryPartyMemberTheSightRadius()
    {
        using var provider = CreateStealthProvider();
        var (caster, casterClient) = CreateRogue(provider, 706);
        var (mate, mateClient) = CreateRogue(provider, 707);
        PartyUp(provider, caster, mate);

        var casterSent = Capture(casterClient);
        var mateSent = Capture(mateClient);

        await CastAsync(provider, caster, LupineEyes);

        SightRadiiIn(casterSent).Should().Equal(SightRadius);
        SightRadiiIn(mateSent).Should().Equal(SightRadius);
        mate.ActiveBuffs.Should().ContainKey(LupineEyes);
    }

    [Fact]
    public async Task LupineEyesWithoutAPartyStillShowsItsIconToTheCaster()
    {
        using var provider = CreateStealthProvider();
        var (caster, casterClient) = CreateRogue(provider, 708);
        var casterSent = Capture(casterClient);

        await CastAsync(provider, caster, LupineEyes);

        SightRadiiIn(casterSent).Should().Equal(SightRadius);
        caster.ActiveBuffs.Should().ContainKey(LupineEyes);
        EffectingDurationsIn(casterSent, LupineEyes).Should().Equal(50);
    }

    [Fact]
    public async Task AnExpiringStealthAnswersWithThreeBytes()
    {
        using var provider = CreateStealthProvider();
        var (session, client) = CreateRogue(provider, 708);

        await CastAsync(provider, session, Hide);
        var sent = Capture(client);

        await provider.GetRequiredService<IMagicExecutionService>().CancelAsync(session, Hide);

        var body = sent
            .Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
            .GetBytes();

        body.Length.Should().Be(3, "the duration-expired reply carries only its code");
        body[2].Should().Be((byte)DurationExpiredCode.Stealth);
    }

    [Fact]
    public async Task SightEndsByClearingTheRadiusItGranted()
    {
        using var provider = CreateStealthProvider();
        var (session, client) = CreateRogue(provider, 709);

        await CastAsync(provider, session, CatsEyes);
        var sent = Capture(client);

        await provider.GetRequiredService<IMagicExecutionService>().CancelAsync(session, CatsEyes);

        SightRadiiIn(sent).Should().Equal((short)0);
    }

    private static ServiceProvider CreateStealthProvider() => CreateProvider(
        _ => { },
        gameData =>
        {
            gameData.GetMagic(Hide).Returns(StealthMagic(Hide));
            gameData.GetMagic(Stealth).Returns(StealthMagic(Stealth));
            gameData.GetMagic(CatsEyes).Returns(StealthMagic(CatsEyes));
            gameData.GetMagic(LupineEyes).Returns(StealthMagic(LupineEyes));
            gameData.GetMagic(Inferno).Returns(new MagicData
            {
                Id = Inferno,
                Type1 = (byte)MagicSkillType.OverTime,
                Moral = (byte)SkillMoral.Enemy,
            });

            gameData.MagicType9Table.Returns(new Dictionary<int, MagicType9Data>
            {
                [Hide] = Type9(Hide, MagicStealthType.DispelOnMove, radius: 0, duration: 40),
                [Stealth] = Type9(Stealth, MagicStealthType.DispelOnAttack, radius: 0, duration: 80),
                [CatsEyes] = Type9(CatsEyes, MagicStealthType.SeeInvisible, SightRadius, duration: 50),
                [LupineEyes] = Type9(LupineEyes, MagicStealthType.SeeInvisibleParty, SightRadius, duration: 50),
            });
            gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data>
            {
                [Inferno] = new() { Id = Inferno },
            });
            gameData.GetCoefficient(Arg.Any<short>()).Returns(CreateBasicCoefficient(105));
        });

    private static MagicData StealthMagic(int skillId) => new()
    {
        Id = skillId,
        Type1 = (byte)MagicSkillType.Stealth,
        Moral = (byte)SkillMoral.Self,
    };

    private static MagicType9Data Type9(
        int skillId, MagicStealthType stealthType, short radius, short duration) => new()
    {
        Id = skillId,
        StateChange = (byte)stealthType,
        Radius = radius,
        Duration = duration,
    };

    private static (UserSession Session, IClient Client) CreateRogue(
        ServiceProvider provider, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 100);
        session.Name = $"Rogue{characterId}";
        session.Class = 105;
        session.Level = 40;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.X = 100;
        session.Z = 100;
        session.Hp = 300;
        session.MaxHp = 300;
        sessionManager.Regions.AddToRegion(session);
        return (session, client);
    }

    private static void PartyUp(ServiceProvider provider, UserSession leader, UserSession member)
    {
        var parties = provider.GetRequiredService<SessionManager>().Parties;
        var party = parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;
    }

    private static List<Packet> Capture(IClient client)
    {
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return sent;
    }

    private static Task CastAsync(ServiceProvider provider, UserSession session, int skillId)
    {
        var magic = provider.GetRequiredService<IGameDataService>().GetMagic(skillId)!;
        return provider.GetRequiredService<IMagicExecutionService>()
            .ExecuteAsync(session, magic, skillId, session.CharacterId, new int[7]);
    }

    private static Task MoveAsync(ServiceProvider provider, UserSession session, ushort x, ushort z)
    {
        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.WriteUShort(0);
        packet.WriteShort(0);
        packet.WriteByte(3);

        return provider.GetRequiredService<IWorldMovementService>()
            .HandleMoveAsync(session.Client, packet);
    }

    private static Task SwingAsync(ServiceProvider provider, IClient client)
    {
        var packet = new Packet(GameOpcodes.GS_ATTACK);
        packet.WriteByte(1);
        packet.WriteByte(0);
        packet.WriteInt(-1);
        packet.WriteShort(400);
        packet.WriteShort(1);
        packet.WriteByte(0);

        return provider.GetRequiredService<ICombatPacketCoordinator>()
            .HandleAttackAsync(client, packet);
    }

    private static List<byte> StealthStatesIn(List<Packet> sent) => sent
        .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE)
        .Select(packet => packet.GetBytes())
        .Where(body => body[5] == (byte)StateChangeType.Stealth)
        .Select(body => body[6])
        .ToList();

    private static List<int> EffectingDurationsIn(List<Packet> sent, int skillId) => sent
        .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS)
        .Select(packet => packet.GetBytes())
        .Where(body => body[1] == (byte)MagicProcessOpcode.Effecting && BitConverter.ToInt32(body, 2) == skillId)
        .Select(body => BitConverter.ToInt32(body, 26))
        .ToList();

    private static List<short> SightRadiiIn(List<Packet> sent) => sent
        .Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_STEALTH)
        .Select(packet => packet.GetBytes())
        .Select(body => body[1] == 0 ? (short)0 : BitConverter.ToInt16(body, 2))
        .ToList();

}
