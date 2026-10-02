using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class KnightsInviteTests : GameTestBase
{
    private const short ClanId = 91;

    private static (IClient Client, List<Packet> Sent) NewClient()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return (client, sent);
    }

    private static ServiceProvider Seed()
    {
        var provider = CreateProvider(db =>
        {
            db.Set<KnightsEntity>().Add(new KnightsEntity
            {
                Id = ClanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
                Flag = (byte)ClanType.Promoted, Members = 1, Cape = 133, CapeR = 1, CapeG = 2, CapeB = 3,
            });
            db.Characters.AddRange(
                new Character { AccountId = 1, Slot = 0, Name = "Aurelia", Level = 70, Class = 201, MapId = 21, KnightsId = ClanId, Fame = 1 },
                new Character { AccountId = 2, Slot = 0, Name = "Dorin", Level = 60, Class = 202, MapId = 21, Loyalty = 72_000 });
        });

        provider.GetRequiredService<SessionManager>().Knights.AddClan(ClanId, new KnightsEntity
        {
            Id = ClanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
            Flag = (byte)ClanType.Promoted, Members = 1, Cape = 133, CapeR = 1, CapeG = 2, CapeB = 3,
        });
        return provider;
    }

    private static async Task<(UserSession Chief, List<Packet> ChiefSent, UserSession Recruit, List<Packet> RecruitSent)> Sessions(ServiceProvider provider)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (chiefClient, chiefSent) = NewClient();
        var chief = sessionManager.CreateSession(chiefClient, await GetCharacterIdAsync(provider, "Aurelia"), accountId: 1);
        chief.Name = "Aurelia";
        chief.Nation = AccountNation.ElMorad;
        chief.ZoneId = (byte)ZoneId.Moradon;
        chief.KnightsId = ClanId;
        chief.KnightsFame = ClanRules.FameChief;
        chief.KnightsName = "Wolves";
        chief.Hp = 100;
        chief.Level = 70;
        chief.Class = 201;

        var (recruitClient, recruitSent) = NewClient();
        var recruit = sessionManager.CreateSession(recruitClient, await GetCharacterIdAsync(provider, "Dorin"), accountId: 2);
        recruit.Name = "Dorin";
        recruit.Nation = AccountNation.ElMorad;
        recruit.ZoneId = (byte)ZoneId.Moradon;
        recruit.Hp = 100;
        recruit.Loyalty = 72_000;
        return (chief, chiefSent, recruit, recruitSent);
    }

    [Fact]
    public async Task AChiefInvitesByCharacterIdAndTheTargetIsPromptedWithTheClanName()
    {
        using var provider = Seed();
        var (chief, chiefSent, recruit, recruitSent) = await Sessions(provider);

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)KnightsSubOpcode.Join);
        packet.WriteInt(recruit.CharacterId);
        await provider.GetRequiredService<IKnightsPacketCoordinator>().HandleProcessAsync(chief.Client, packet);

        chiefSent.Should().BeEmpty();
        var prompt = recruitSent.Should().ContainSingle().Subject;
        prompt.ResetOffset();
        prompt.ReadByte().Should().Be((byte)KnightsSubOpcode.Invite);
        prompt.ReadByte().Should().Be(1);
        prompt.ReadInt().Should().Be(chief.CharacterId);
        prompt.ReadShort().Should().Be(ClanId);
        prompt.ReadString().Should().Be("Wolves");
        recruit.ClanInviteFrom.Should().Be(chief.CharacterId);
    }

    [Fact]
    public async Task AcceptingTheInviteJoinsTheClanAndTellsTheRegion()
    {
        using var provider = Seed();
        var (chief, _, recruit, recruitSent) = await Sessions(provider);
        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();

        var invite = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        invite.WriteByte((byte)KnightsSubOpcode.Join);
        invite.WriteInt(recruit.CharacterId);
        await coordinator.HandleProcessAsync(chief.Client, invite);
        recruitSent.Clear();

        var answer = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        answer.WriteByte((byte)KnightsSubOpcode.Invite);
        answer.WriteByte(1);
        answer.WriteInt(chief.CharacterId);
        answer.WriteShort(ClanId);
        await coordinator.HandleProcessAsync(recruit.Client, answer);

        recruit.KnightsId.Should().Be(ClanId);
        recruit.KnightsFame.Should().Be(ClanRules.FameTrainee);
        recruit.KnightsName.Should().Be("Wolves");
        recruit.ClanInviteFrom.Should().Be(0);

        var joined = recruitSent.Single(sent => sent.GetData()[0] == (byte)KnightsSubOpcode.Join);
        joined.ResetOffset();
        joined.ReadByte();
        joined.ReadByte().Should().Be(1);
        joined.ReadInt().Should().Be(recruit.CharacterId);
        joined.ReadShort().Should().Be(ClanId);
        joined.ReadByte().Should().Be(ClanRules.FameTrainee);
        joined.ReadByte().Should().Be((byte)ClanType.Promoted);
        joined.ReadShort().Should().Be(0);
        joined.ReadShort().Should().Be(133);
        joined.ReadInt().Should().Be(0x030201);
        joined.ReadShort();
        joined.ReadString().Should().Be("Wolves");
        joined.ReadByte().Should().Be(4);
        joined.ReadByte().Should().Be(1);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Characters.SingleAsync(c => c.Name == "Dorin")).KnightsId.Should().Be(ClanId);
        (await db.Set<KnightsEntity>().SingleAsync(c => c.Id == ClanId)).Members.Should().Be(2);
    }

    [Fact]
    public async Task DecliningTellsTheInviterTheUserDeclined()
    {
        using var provider = Seed();
        var (chief, chiefSent, recruit, _) = await Sessions(provider);
        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();

        var invite = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        invite.WriteByte((byte)KnightsSubOpcode.Join);
        invite.WriteInt(recruit.CharacterId);
        await coordinator.HandleProcessAsync(chief.Client, invite);

        var answer = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        answer.WriteByte((byte)KnightsSubOpcode.Invite);
        answer.WriteByte(0);
        answer.WriteInt(chief.CharacterId);
        answer.WriteShort(ClanId);
        await coordinator.HandleProcessAsync(recruit.Client, answer);

        recruit.KnightsId.Should().Be(0);
        var refusal = chiefSent.Should().ContainSingle().Subject;
        refusal.ResetOffset();
        refusal.ReadByte().Should().Be((byte)KnightsSubOpcode.Join);
        refusal.ReadByte().Should().Be((byte)KnightsResult.UserDeclined);
    }

    [Fact]
    public async Task OnlyAChiefOrViceChiefMayInvite()
    {
        using var provider = Seed();
        var (chief, chiefSent, recruit, recruitSent) = await Sessions(provider);
        chief.KnightsFame = ClanRules.FameTrainee;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)KnightsSubOpcode.Join);
        packet.WriteInt(recruit.CharacterId);
        await provider.GetRequiredService<IKnightsPacketCoordinator>().HandleProcessAsync(chief.Client, packet);

        recruitSent.Should().BeEmpty();
        var refusal = chiefSent.Should().ContainSingle().Subject;
        refusal.ResetOffset();
        refusal.ReadByte().Should().Be((byte)KnightsSubOpcode.Join);
        refusal.ReadByte().Should().Be((byte)KnightsResult.NoAuthority);
    }

    [Fact]
    public async Task TheMemberListCarriesTheRetailHeader()
    {
        using var provider = Seed();
        var (chief, chiefSent, _, _) = await Sessions(provider);
        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.Notice = "raid at eight";

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)KnightsSubOpcode.MemberRequest);
        await provider.GetRequiredService<IKnightsPacketCoordinator>().HandleProcessAsync(chief.Client, packet);

        var list = chiefSent.Single(sent => sent.GetData()[0] == (byte)KnightsSubOpcode.MemberRequest);
        list.ResetOffset();
        list.ReadByte();
        list.ReadByte().Should().Be(1);
        list.ReadShort().Should().Be(0);
        list.ReadShort().Should().Be(1);
        list.ReadShort().Should().Be(ClanRules.MaxMembers);
        list.ReadString().Should().Be("raid at eight");
        list.ReadShort().Should().Be(1);
        list.ReadString().Should().Be("Aurelia");
        list.ReadByte().Should().Be(ClanRules.FameChief);
        list.ReadSByteString().Should().Be(string.Empty);
        list.ReadByte().Should().Be(70);
        list.ReadShort().Should().Be(201);
        list.ReadByte().Should().Be(1);
        list.ReadString().Should().Be(string.Empty);
        list.ReadInt().Should().Be(0);
        list.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ThePointWindowOpensWithThePlayersPointsAndTheFund()
    {
        using var provider = Seed();
        var (chief, chiefSent, _, _) = await Sessions(provider);
        chief.Loyalty = 12_345;
        provider.GetRequiredService<SessionManager>().Knights.GetClan(ClanId)!.ClanPointFund = 250_000;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)KnightsSubOpcode.PointRequest);
        await provider.GetRequiredService<IKnightsPacketCoordinator>().HandleProcessAsync(chief.Client, packet);

        var reply = chiefSent.Should().ContainSingle().Subject;
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)KnightsSubOpcode.PointRequest);
        reply.ReadByte().Should().Be(1);
        reply.ReadInt().Should().Be(12_345);
        reply.ReadInt().Should().Be(250_000);
    }
}
