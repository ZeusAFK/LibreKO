using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class KairaPremiumTests : GameTestBase
{
    private const short Kaira = 29056;
    private const byte KairaNpcType = 47;
    private const byte Moradon = 21;
    private const int PremiumDays = 30;

    private static string QuestsDirectory([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests"));

    private static QuestProgram KairaProgram()
    {
        var result = QuestCompilation.CreateFromFile(Path.Combine(QuestsDirectory(), "29056_21.quest"));
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        return result.Program;
    }

    private static void Redeem(IQuestHost host, int eventId)
    {
        var program = KairaProgram();
        program.EventNames.TryGetValue($"event_{eventId}", out var compiledId).Should().BeTrue();
        new QuestInterpreter(program, host).Run(compiledId).Failure.Should().BeNull();
    }

    [Theory]
    [InlineData(411, 399281000, 10)]
    [InlineData(412, 399282000, 11)]
    [InlineData(413, 399292000, 12)]
    [InlineData(414, 399295000, 13)]
    [InlineData(415, 800880000, 7)]
    [InlineData(416, 800080000, 5)]
    [InlineData(417, 814042000, 3)]
    public void APremiumBoughtInTheStoreIsTakenAndGrantsThirtyDays(int eventId, int itemId, int premiumType)
    {
        var host = Substitute.For<IQuestHost>();
        host.ItemCount(itemId).Returns(1);

        Redeem(host, eventId);

        host.Received(1).TakeItem(itemId, 1);
        host.Received(1).GivePremium(premiumType, PremiumDays);
    }

    [Theory]
    [InlineData(411, 399281685, 10)]
    [InlineData(412, 399282686, 11)]
    [InlineData(413, 399292764, 12)]
    [InlineData(414, 399295859, 13)]
    public void TheOlderPremiumItemsStillRedeem(int eventId, int itemId, int premiumType)
    {
        var host = Substitute.For<IQuestHost>();
        host.ItemCount(itemId).Returns(1);

        Redeem(host, eventId);

        host.Received(1).TakeItem(itemId, 1);
        host.Received(1).GivePremium(premiumType, PremiumDays);
    }

    [Fact]
    public void WithoutThePremiumItemNothingIsTakenOrGranted()
    {
        var host = Substitute.For<IQuestHost>();

        Redeem(host, 412);

        host.DidNotReceive().TakeItem(Arg.Any<int>(), Arg.Any<int>());
        host.DidNotReceive().GivePremium(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task KairaOffersThePremiumTopicInMoradon()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetNpc(Kaira, Arg.Any<bool>()).Returns(new NpcData { Id = Kaira, Name = "[Vendor] Kaira", NpcType = KairaNpcType, IsMonster = false });
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
        session.Name = "Buyer";
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.Class = 206;
        session.Level = 60;
        session.Nation = AccountNation.ElMorad;
        session.X = 800;
        session.Z = 400;
        sessionManager.Regions.AddToRegion(session);

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = Kaira, Name = "[Vendor] Kaira", NpcType = KairaNpcType, ZoneId = Moradon,
            X = 800, Z = 400, SpawnX = 800, SpawnZ = 400, Hp = 100, MaxHp = 100,
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(1);
        packet.WriteInt(npc.UniqueId);
        packet.ResetOffset();

        await provider.GetRequiredService<IQuestNpcInteractionService>().HandleNpcEventAsync(client, packet);

        var dialog = sent.Should().ContainSingle(p => p.GetOpcode() == (byte)GameOpcodes.GS_SELECT_MSG).Subject;
        System.Text.Encoding.UTF8.GetString(dialog.GetBytes()).Should().Contain("[Premium Item Use]");
    }
}
