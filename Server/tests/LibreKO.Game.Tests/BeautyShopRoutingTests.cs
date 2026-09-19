using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BeautyShopRoutingTests : GameTestBase
{
    private const short Knoker = 29071;
    private const byte ElMorad = 2;
    private const byte Moradon = 21;
    private const short DispatchingInspectors = 780;
    private const short Sign = 779;

    private static string QuestsDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests"));

    [Fact]
    public async Task KellyOpensTheBeautyShop()
    {
        var sent = await TalkTo(NpcData.MakeupArtist, "[Makeup Artist] Kelly", Moradon, AccountNation.ElMorad);

        var shop = sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHANGE_HAIR).Subject;
        shop.ReadByte().Should().Be(PreGamePacketWriter.ChangeHairOpenShop);
        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_SELECT_MSG);
    }

    [Fact]
    public async Task AnotherTalkNpcOfTheSameTypeGreetsWithItsQuests()
    {
        var sent = await TalkTo(Knoker, "[Order of the Sorcery] Knoker", ElMorad, AccountNation.ElMorad,
            session =>
            {
                session.Level = 51;
                session.Quest.QuestMap[Sign] = 2;
            });

        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHANGE_HAIR,
            "type 64 is the plain talk type shared by a hundred quest NPCs, not the beauty shop");
        var dialog = sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_SELECT_MSG).Subject;
        System.Text.Encoding.UTF8.GetString(dialog.GetBytes()).Should().Contain("Dispatching Inspectors");
    }

    private static async Task<List<Packet>> TalkTo(short npcId, string name, byte zone, AccountNation nation,
        Action<UserSession>? arrange = null)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetNpc(npcId, Arg.Any<bool>()).Returns(new NpcData { Id = npcId, Name = name, NpcType = NpcData.TypeTalk, IsMonster = false });
        }, settings =>
        {
            settings.QuestsDirectory = QuestsDirectory();
            settings.QuestManifest = "quest-manifest.json";
        });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 920, accountId: 930);
        session.Name = "Talker";
        session.ZoneId = zone;
        session.Hp = 100;
        session.Class = 206;
        session.Level = 60;
        session.Nation = nation;
        session.X = 1104;
        session.Z = 1905;
        arrange?.Invoke(session);
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = npcId, Name = name, NpcType = NpcData.TypeTalk, ZoneId = zone,
            X = 1104, Z = 1905, SpawnX = 1104, SpawnZ = 1905, Hp = 100, MaxHp = 100,
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(1);
        packet.WriteInt(npc.UniqueId);
        packet.ResetOffset();

        await provider.GetRequiredService<IQuestNpcInteractionService>().HandleNpcEventAsync(client, packet);
        return sent;
    }
}
