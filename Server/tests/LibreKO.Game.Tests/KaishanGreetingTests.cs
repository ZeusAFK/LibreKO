using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace LibreKO.Game.Tests;

public class KaishanGreetingTests(ITestOutputHelper output) : GameTestBase
{
    private const short Kaishan = 18004;
    private const byte Moradon = 21;

    private static string QuestsDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests"));

    [Fact]
    public async Task AMaxLevelCharacterStillGetsKaishansRedistributionsTopic()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetNpc(Kaishan, Arg.Any<bool>()).Returns(new NpcData { Id = Kaishan, Name = "[Grand Merchant] Kaishan", NpcType = 46, IsMonster = false });
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
        var session = sessionManager.CreateSession(client, characterId: 900, accountId: 910);
        session.Name = "Talker";
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.Class = 206;
        session.Level = 83;
        session.Quest.QuestMap[71] = 2;
        session.Nation = AccountNation.ElMorad;
        session.X = 1665;
        session.Z = 396;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = Kaishan, Name = "[Grand Merchant] Kaishan", NpcType = 46, ZoneId = Moradon,
            X = 1665, Z = 396, SpawnX = 1665, SpawnZ = 396, Hp = 100, MaxHp = 100,
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(1);
        packet.WriteInt(npc.UniqueId);
        packet.ResetOffset();

        await provider.GetRequiredService<IQuestNpcInteractionService>().HandleNpcEventAsync(client, packet);

        var dialog = sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_SELECT_MSG).Subject;
        var body = System.Text.Encoding.UTF8.GetString(dialog.GetBytes());
        output.WriteLine(body);
        body.Should().Contain("Redistributions", "Kaishan redistributes stat and mastery points for anyone past level 10");
    }
}
