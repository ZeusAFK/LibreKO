using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyMemberBuffsTests : GameTestBase
{
    private const byte Moradon = 21;
    private const int FirstSkill = 101_010;
    private const int SecondSkill = 112_675;
    private const int JoinerSkill = 500_049;

    [Fact]
    public void ABuffChangeOnAMemberReachesTheRestOfTheParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        _ = provider.GetRequiredService<ICombatNotificationService>();

        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 600, leaderSent);
        var member = CreateMember(sessionManager, 601);
        var outsiderSent = new List<Packet>();
        CreateMember(sessionManager, 602, outsiderSent);
        FormParty(sessionManager, leader, member);

        member.SetBuff(FirstSkill, Lasting(FirstSkill));
        member.SetBuff(SecondSkill, Lasting(SecondSkill));

        BuffListsIn(leaderSent).Should().Equal(
            Listed(member, FirstSkill),
            Listed(member, FirstSkill, SecondSkill));
        BuffListsIn(outsiderSent).Should().BeEmpty();

        leaderSent.Clear();
        member.RemoveBuff(FirstSkill);

        BuffListsIn(leaderSent).Should().Equal(Listed(member, SecondSkill));
    }

    [Fact]
    public void ExpiredBuffsAreLeftOutOfTheList()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        _ = provider.GetRequiredService<ICombatNotificationService>();

        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 610, leaderSent);
        var member = CreateMember(sessionManager, 611);
        FormParty(sessionManager, leader, member);

        member.SetBuff(FirstSkill, new ActiveBuff { MagicId = FirstSkill, ExpireTicks = DateTime.UtcNow.AddSeconds(-1).Ticks });

        BuffListsIn(leaderSent).Should().Equal(Listed(member));
    }

    [Fact]
    public void AnInviteeWhoHasNotAcceptedSendsNoListToTheParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        _ = provider.GetRequiredService<ICombatNotificationService>();

        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 630, leaderSent);
        var member = CreateMember(sessionManager, 631);
        var party = FormParty(sessionManager, leader, member);
        var invitee = CreateMember(sessionManager, 632);
        invitee.InvitedToParty = party.Index;

        invitee.SetBuff(FirstSkill, Lasting(FirstSkill));

        BuffListsIn(leaderSent).Should().BeEmpty();
    }

    [Fact]
    public void OutsideAPartyABuffChangeSendsNoList()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        _ = provider.GetRequiredService<ICombatNotificationService>();

        var sent = new List<Packet>();
        var loner = CreateMember(sessionManager, 620, sent);

        loner.SetBuff(FirstSkill, Lasting(FirstSkill));

        BuffListsIn(sent).Should().BeEmpty();
    }

    [Fact]
    public async Task AJoinerGetsEveryMembersBuffsAndThePartyGetsTheJoiners()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 630, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 631, memberSent);
        var joinerSent = new List<Packet>();
        var joiner = CreateMember(sessionManager, 632, joinerSent);
        leader.SetBuff(FirstSkill, Lasting(FirstSkill));
        member.SetBuff(SecondSkill, Lasting(SecondSkill));
        joiner.SetBuff(JoinerSkill, Lasting(JoinerSkill));
        var party = FormParty(sessionManager, leader, member);
        joiner.InvitedToParty = party.Index;
        leaderSent.Clear();
        memberSent.Clear();

        await coordinator.HandleAsync(joiner.Client, Accept());

        BuffListsIn(joinerSent).Should().Equal(
            Listed(leader, FirstSkill),
            Listed(member, SecondSkill),
            Listed(joiner, JoinerSkill));
        BuffListsIn(leaderSent).Should().Equal(Listed(joiner, JoinerSkill));
        BuffListsIn(memberSent).Should().Equal(Listed(joiner, JoinerSkill));
    }

    private static ActiveBuff Lasting(int skillId) => new()
    {
        MagicId = skillId,
        Duration = 600,
        ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
    };

    private static PartyGroup FormParty(SessionManager sessionManager, UserSession leader, UserSession member)
    {
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;
        return party;
    }

    private static Packet Accept()
    {
        var accept = new Packet(GameOpcodes.GS_PARTY);
        accept.WriteByte((byte)PartyRequest.Permit);
        accept.WriteByte(1);
        return accept;
    }

    private static string Listed(UserSession member, params int[] skillIds) =>
        $"{member.CharacterId}:{string.Join(',', skillIds)}";

    private static List<string> BuffListsIn(List<Packet> sent)
    {
        var lists = new List<string>();
        foreach (var packet in sent)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_PARTY)
                continue;

            packet.ResetOffset();
            if (packet.ReadByte() != (byte)PartySubOpcode.MemberBuffs)
                continue;

            var characterId = packet.ReadInt();
            var skillIds = new int[packet.ReadByte()];
            for (var index = 0; index < skillIds.Length; index++)
                skillIds[index] = packet.ReadInt();
            lists.Add($"{characterId}:{string.Join(',', skillIds)}");
        }
        return lists;
    }

    private static UserSession CreateMember(SessionManager sessionManager, int characterId, List<Packet>? sent = null)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        if (sent != null)
            client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        else
            client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 100);
        session.Name = $"Player{characterId}";
        session.Class = 105;
        session.Level = 40;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.X = 100;
        session.Z = 100;
        session.Hp = 300;
        session.MaxHp = 300;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
