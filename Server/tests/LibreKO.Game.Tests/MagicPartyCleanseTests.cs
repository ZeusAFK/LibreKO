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

public class MagicPartyCleanseTests : GameTestBase
{
    private const int BlessOfGod = 112671;
    private const int Curse = 190001;
    private const byte Moradon = 21;
    private const byte RemoveBuffSubtype = 2;
    private const int AreaTarget = -1;

    [Fact]
    public async Task APartyCleanseAimedAtTheAreaLiftsEveryMembersCurses()
    {
        using var provider = CreateProvider(_ => { }, Cleanse);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreatePlayer(sessionManager, 900, x: 100, z: 100);
        var friend = CreatePlayer(sessionManager, 901, x: 112, z: 108);
        var stranger = CreatePlayer(sessionManager, 902, x: 104, z: 104);

        var party = sessionManager.Parties.CreateParty((short)caster.CharacterId);
        party.MemberIds[1] = (short)friend.CharacterId;
        caster.PartyIndex = party.Index;
        caster.IsPartyLeader = true;
        friend.PartyIndex = party.Index;

        foreach (var cursed in new[] { caster, friend, stranger })
            cursed.ActiveBuffs[Curse] = new ActiveBuff
            {
                MagicId = Curse, CasterId = 777, BuffType = BuffType.Blind,
                ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
            };

        await Cast(provider, caster, BlessOfGod, AreaTarget);

        caster.ActiveBuffs.Should().BeEmpty();
        friend.ActiveBuffs.Should().BeEmpty("the whole party is cleansed, not only the caster");
        stranger.ActiveBuffs.Should().ContainKey(Curse, "a player outside the party keeps their curse");
    }

    private static void Cleanse(IGameDataService gameData)
    {
        gameData.GetMagic(BlessOfGod).Returns(new MagicData
        {
            Id = BlessOfGod, Type1 = 5, Moral = (byte)SkillMoral.PartyAll, Range = 0,
        });
        gameData.MagicType5Table.Returns(new Dictionary<int, MagicType5Data>
        {
            [BlessOfGod] = new() { Id = BlessOfGod, Type = RemoveBuffSubtype },
        });
    }

    private static UserSession CreatePlayer(SessionManager sessionManager, int characterId, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var player = sessionManager.CreateSession(client, characterId, characterId + 100);
        player.Name = $"Player{characterId}";
        player.Class = 204;
        player.Level = 70;
        player.Nation = AccountNation.Karus;
        player.ZoneId = Moradon;
        player.X = x;
        player.Z = z;
        player.Hp = 500;
        player.MaxHp = 500;
        player.Mp = 500;
        player.MaxMp = 500;
        sessionManager.Regions.AddToRegion(player);
        return player;
    }

    private static async Task Cast(ServiceProvider provider, UserSession caster, int skillId, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(caster.Client, packet);
    }
}
