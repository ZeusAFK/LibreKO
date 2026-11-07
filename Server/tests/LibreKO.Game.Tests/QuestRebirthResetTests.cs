using FluentAssertions;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestRebirthResetTests
{
    private const int Kaishan = 18004;
    private const int Moradon = 21;
    private const int WarPremium = 12;
    private const int SwitchingPremium = 13;
    private const int ResetFee = 100_000_000;
    private const int PremiumHoursLeft = 24;

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Compose()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("18004_21_1388.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose("kaishan", Kaishan, Moradon, [compilation.Program]);
    }

    private static IQuestHost Reborn(int premium, int coins)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(Moradon);
        host.PlayerRebirthLevel.Returns(1);
        host.PremiumType.Returns(premium);
        host.PlayerCoins.Returns(coins);
        return host;
    }

    [Theory]
    [InlineData(WarPremium, 0, true)]
    [InlineData(SwitchingPremium, 0, true)]
    [InlineData(0, ResetFee, true)]
    [InlineData(0, ResetFee - 1, false)]
    public void YesOpensTheResetWindowWhenThePremiumOrTheFeeAllowsIt(int premium, int coins, bool opens)
    {
        var program = Compose();
        var host = Reborn(premium, coins);
        var yes = program.Events.Values.Single(e => e.Name is null).Id;
        new QuestInterpreter(program, host).Run(yes).Failure.Should().BeNull();
        host.Received(opens ? 1 : 0).OpenRebirthResetPanel();
    }

    [Fact]
    public void TheResetWindowIsDialogStyle52() =>
        NpcDialogPacketWriter.RebirthResetPanelStyle.Should().Be(52);

    [Theory]
    [InlineData(WarPremium, true)]
    [InlineData(SwitchingPremium, true)]
    [InlineData(0, false)]
    public void TheResetIsFreeWithWarOrSwitchingPremium(byte premium, bool free)
    {
        var client = Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = new World.UserSession(client, 1, 1)
        {
            PremiumService = premium,
            HeldPremiums = { [premium] = DateTime.UtcNow.AddHours(PremiumHoursLeft) },
        };
        CharacterDevelopmentPacketCoordinator.IsRebirthResetFree(session).Should().Be(free);
    }
}
