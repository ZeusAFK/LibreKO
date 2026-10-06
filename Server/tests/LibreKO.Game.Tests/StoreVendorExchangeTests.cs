using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class StoreVendorExchangeTests
{
    private const int Kaira = 29056;
    private const int Hemes = 16085;
    private const int Kelly = 31525;
    private const int GenderChangeItem = 810594000;
    private const int Kaishan = 18004;
    private const int NationTransferItem = 810096000;
    private const int Moradon = 21;
    private const int Karus = 1;
    private const int ElMorad = 2;
    private const int ThirtyDays = 30 * 24;
    private const string StoreHint = "Where can I find it?";

    private const int MinervaPackage = 508073000;
    private const int OlderMinervaPackage = 508112000;
    private const int MinervaHelmetCertificate = 508056000;
    private const int MinervaArmorCertificate = 508057000;
    private const int PathosPackage = 508070000;
    private const int PathosCertificate = 800250000;
    private const int SealVoucher = 810520000;
    private const int MediumSealCoupon = 810700000;
    private const int SealedItem = 810890000;
    private const int DragonWingCoupon = 810164000;
    private const int MenissiahCoupon = 810163000;
    private const int MenissiahList = 810166000;
    private const int WarTattooVoucher = 814663000;
    private const int WarTattoo = 814664796;
    private const int GryphonArmorCertificate = 800240000;
    private const int GryphonHelmetCertificate = 800230000;
    private const int ScrollOfIdentity = 800032000;

    private static bool Takes(IReadOnlyList<BoundStatement.Action> actions, int item, int count = 1) =>
        actions.Any(a => a.Kind == QuestActionKind.TakeItem && a.Arguments.GetInt("item") == item
            && a.Arguments.GetInt("amount", a.Arguments.GetInt("count", 1)) == count);

    private static int Gives(IReadOnlyList<BoundStatement.Action> actions, int item, int count = 1, int hours = 0) =>
        actions.Count(a => a.Kind == QuestActionKind.GiveItem && a.Arguments.GetInt("item") == item
            && a.Arguments.GetInt("amount", a.Arguments.GetInt("count", 1)) == count
            && QuestInterpreter.RentalHours(a.Arguments) == hours);

    private static void ShouldTrade(IQuestHost host, int taken, int given, int count = 1, int hours = 0) =>
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 2 && Takes(actions, taken) && Gives(actions, given, count, hours) == 1));

    [Fact]
    public void KairaGreetsWithTheStoreExchanges()
    {
        var talk = new QuestTalk(Kaira, Karus);

        talk.Labels.Should().Equal("[Premium Item Use]", "[Exchange][Minerva Package]", "[Exchange][Pathos Glove Package]",
            "[Seal Exchange Coupon]", "[Exchange][Dragon's Wings]", "Exchanging items");
        talk.Follow("Exchanging items").Labels.Should().Equal("[Exchange][Menissiah's Trade Paper]",
            "[Exchange] [Spirit's help]", "War Tattoo (30 Days)", "Cancel");
    }

    [Theory]
    [InlineData(MinervaPackage)]
    [InlineData(OlderMinervaPackage)]
    public void TheMinervaPackageOpensIntoBothCertificates(int package)
    {
        var talk = new QuestTalk(Kaira, Karus, package).Follow("[Exchange][Minerva Package]", "Exchange");

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 3 && Takes(actions, package)
            && Gives(actions, MinervaHelmetCertificate) == 1 && Gives(actions, MinervaArmorCertificate) == 1));
    }

    [Fact]
    public void ThePathosPackageOpensIntoTwoCertificates()
    {
        var talk = new QuestTalk(Kaira, Karus, PathosPackage).Follow("[Exchange][Pathos Glove Package]", "Exchange");

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 3 && Takes(actions, PathosPackage) && Gives(actions, PathosCertificate) == 2));
    }

    [Theory]
    [InlineData("[Seal 10 voucher]", SealVoucher, 10)]
    [InlineData("[Seal 50 voucher]", MediumSealCoupon, 50)]
    public void ASealCouponTurnsIntoSealedItems(string topic, int coupon, int sealedItems)
    {
        var talk = new QuestTalk(Kaira, Karus, coupon).Follow("[Seal Exchange Coupon]", topic, "Yes");

        ShouldTrade(talk.Host, coupon, SealedItem, sealedItems);
    }

    [Theory]
    [InlineData("Nation Point +3 (Restrictions apply)", Karus, 810178835)]
    [InlineData("Defense +50", Karus, 810178836)]
    [InlineData("HP +300", Karus, 810178837)]
    [InlineData("Base Experience for Monster Kills +9%", Karus, 810178838)]
    [InlineData("Nation Point +3 (Restrictions apply)", ElMorad, 810179839)]
    [InlineData("Defense +50", ElMorad, 810179840)]
    [InlineData("HP +300", ElMorad, 810179841)]
    [InlineData("Base Experience for Monster Kills +9%", ElMorad, 810179842)]
    public void TheDragonCouponGivesTheNationsWingWithTheChosenAttribute(string attribute, int nation, int wing)
    {
        var talk = new QuestTalk(Kaira, nation, DragonWingCoupon).Follow("[Exchange][Dragon's Wings]", attribute);

        ShouldTrade(talk.Host, DragonWingCoupon, wing, hours: ThirtyDays);
    }

    [Fact]
    public void WithoutTheCouponKairaPointsAtTheStore()
    {
        var talk = new QuestTalk(Kaira, Karus).Follow("[Exchange][Dragon's Wings]");

        talk.Header!.Text.Should().StartWith("You don't seem to have [Dragon's Wings Vouchers].");
        talk.Labels.Should().Equal(StoreHint);
        talk.Host.DidNotReceive().ApplyReward(Arg.Any<IReadOnlyList<BoundStatement.Action>>());
    }

    [Fact]
    public void TheMenissiahCouponRentsTheListForThirtyDays()
    {
        var talk = new QuestTalk(Kaira, Karus, MenissiahCoupon)
            .Follow("Exchanging items", "[Exchange][Menissiah's Trade Paper]", "Exchange");

        ShouldTrade(talk.Host, MenissiahCoupon, MenissiahList, hours: ThirtyDays);
    }

    [Theory]
    [InlineData("Special Nereids (Defense) Exchange", 811135000, 1340711000)]
    [InlineData("Special Nereids (Attack) Exchange", 811136000, 1340712000)]
    public void ASpecialNereidsVoucherWakesItsSpirit(string topic, int voucher, int spirit)
    {
        var talk = new QuestTalk(Kaira, ElMorad, voucher).Follow("Exchanging items", "[Exchange] [Spirit's help]", topic, "Yes");

        ShouldTrade(talk.Host, voucher, spirit, hours: ThirtyDays);
    }

    [Fact]
    public void TheWarTattooVoucherGivesTheTattoo()
    {
        var talk = new QuestTalk(Kaira, Karus, WarTattooVoucher).Follow("Exchanging items", "War Tattoo (30 Days)", "Yes");

        ShouldTrade(talk.Host, WarTattooVoucher, WarTattoo, hours: ThirtyDays);
    }

    [Fact]
    public void HemesGreetsWithTheCertificateServices()
    {
        new QuestTalk(Hemes, Karus).Labels.Should().Equal("Change ID", "Minerva Clothing", "Existing Minerva Clothing", "Pathos' Glove");
    }

    [Theory]
    [InlineData("Gryphon's Armor", GryphonArmorCertificate, "HP +200", 508471453)]
    [InlineData("Gryphon's Armor", GryphonArmorCertificate, "Defense Power +30", 508471454)]
    [InlineData("Gryphon's Armor", GryphonArmorCertificate, "Increases Base Experience for Monster Kills gained by 4%", 508471455)]
    [InlineData("Gryphon's Armor", GryphonArmorCertificate, "Base Noah for Monster Kills  drop rate +6%", 508471456)]
    [InlineData("Gryphon's Helmet", GryphonHelmetCertificate, "HP +100", 508473453)]
    [InlineData("Gryphon's Helmet", GryphonHelmetCertificate, "Defense Power +20", 508473454)]
    [InlineData("Gryphon's Helmet", GryphonHelmetCertificate, "Increases Base Experience for Monster Kills gained by 2%", 508473455)]
    [InlineData("Gryphon's Helmet", GryphonHelmetCertificate, "Base Noah for Monster Kills  drop rate +3%", 508473456)]
    public void AGryphonCertificateGivesThePieceWithTheChosenOption(string piece, int certificate, string option, int item)
    {
        var talk = new QuestTalk(Hemes, Karus, certificate).Follow("Existing Minerva Clothing", piece, option, "Receive item");

        ShouldTrade(talk.Host, certificate, item, hours: ThirtyDays);
    }

    [Fact]
    public void WithoutTheGryphonCertificateHemesPointsAtTheStore()
    {
        var talk = new QuestTalk(Hemes, Karus).Follow("Existing Minerva Clothing", "Gryphon's Armor");

        talk.Header!.Text.Should().StartWith("You need a [Gryphon's Armor Voucher]");
        talk.Labels.Should().Equal(StoreHint);
        talk.Host.DidNotReceive().ApplyReward(Arg.Any<IReadOnlyList<BoundStatement.Action>>());
    }

    [Theory]
    [InlineData("Valkyrie Armor", MinervaArmorCertificate, "HP +200", 508011441)]
    [InlineData("Valkyrie Helmet", MinervaHelmetCertificate, "Defense Power +20", 508013319)]
    [InlineData("Gryphon's Armor", MinervaArmorCertificate, "Increases Base Experience for Monster Kills gained by 4%", 508471455)]
    [InlineData("Bahamut Helmet", MinervaHelmetCertificate, "Base Noah for Monster Kills  drop rate +3%", 508053469)]
    [InlineData("Transparent Minerva Armor", MinervaArmorCertificate, "Defense Power +30", 508066592)]
    [InlineData("Transparent Minerva Helmet", MinervaHelmetCertificate, "HP +100", 508065591)]
    public void AMinervaCertificateBecomesTheChosenLook(string look, int certificate, string option, int item)
    {
        var talk = new QuestTalk(Hemes, ElMorad, certificate).Follow("Minerva Clothing", look, option, "Receive item");

        ShouldTrade(talk.Host, certificate, item, hours: ThirtyDays);
    }

    [Theory]
    [InlineData("Attack Aurora", "3% damage increase to Warrior Class", 502573462)]
    [InlineData("Attack Aurora", "3% damage increase to Priest Class", 505573465)]
    [InlineData("Defense Aurora", "3% defense increase against Rogue Class", 512573472)]
    [InlineData("Defense Aurora", "3% defense increase against Magician Class", 513573473)]
    public void APathosCertificateGivesTheChosenGlove(string aurora, string option, int glove)
    {
        var talk = new QuestTalk(Hemes, Karus, PathosCertificate).Follow("Pathos' Glove", aurora, option, "Receive item");

        ShouldTrade(talk.Host, PathosCertificate, glove, hours: ThirtyDays);
        talk.Header!.Text.Should().Be("Thank you. Enjoy your adventure");
    }

    [Fact]
    public void TheScrollOfIdentityOpensTheRenameWindow()
    {
        var talk = new QuestTalk(Hemes, Karus, ScrollOfIdentity).Follow("Change ID");

        talk.Host.Received(1).OpenRenamePanel();
    }

    [Fact]
    public void WithoutTheScrollHemesExplainsWhereToBuyIt()
    {
        var talk = new QuestTalk(Hemes, Karus).Follow("Change ID");

        talk.Host.DidNotReceive().OpenRenamePanel();
        talk.Header!.Text.Should().StartWith("To change your ID you need the [ID Change Scroll].");
    }

    [Fact]
    public void KellyOpensTheGenderWindowForTheItem()
    {
        var talk = new QuestTalk(Kelly, ElMorad, GenderChangeItem);

        talk.Labels.Should().Equal("Gender Change");
        talk.Follow("Gender Change").Host.Received(1).OpenGenderChangePanel();
    }

    [Fact]
    public void WithoutTheItemKellyPointsAtTheStore()
    {
        var talk = new QuestTalk(Kelly, ElMorad).Follow("Gender Change");

        talk.Host.DidNotReceive().OpenGenderChangePanel();
        talk.Labels.Should().Equal(StoreHint);
    }

    [Fact]
    public void TheGenderWindowIsDialogStyleFiftyThree()
    {
        var packet = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.GenderChangePanel(Kelly, "31525_21");
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(53);
    }

    [Fact]
    public void KaishanOpensTheTransferForTheCertificate()
    {
        var talk = new QuestTalk("18004_21_76.quest", "event_699", Kaishan, Karus, NationTransferItem);

        talk.Labels.Should().Equal("Move out", "Reconsider");
        talk.Follow("Move out").Host.Received(1).OpenNationTransferPanel();
    }

    [Fact]
    public void WithoutTheCertificateKaishanPointsAtTheStore()
    {
        var talk = new QuestTalk("18004_21_76.quest", "event_699", Kaishan, Karus);

        talk.Labels.Should().Equal(StoreHint);
        talk.Host.DidNotReceive().OpenNationTransferPanel();
    }
}
