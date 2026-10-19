using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class CharacterSealTests
{
    private const int Ring = 800112000;
    private const int GearSlots = 17;

    private static Packet Reply(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case ushort u: p.WriteUShort(u); break;
                case int i: p.WriteInt(i); break;
                case string text: p.WriteString(text); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    private static void WriteSlot(Packet p, string name, byte race, short cls, byte level)
    {
        p.WriteString(name);
        p.WriteByte(race);
        p.WriteShort(cls);
        p.WriteByte(level);
        p.WriteByte(0);
        p.WriteByte(0);
        p.WriteInt(0);
        p.WriteShort(21);
        for (int i = 0; i < GearSlots; i++)
        {
            p.WriteInt(0);
            p.WriteShort(0);
        }
    }

    private static void WriteSummary(Packet p, string name, byte cls, byte level, ushort exp, ushort race, byte rebirth)
    {
        p.WriteString(name);
        p.WriteByte(cls);
        p.WriteByte(level);
        p.WriteUShort(exp);
        p.WriteUShort(race);
        p.WriteByte(rebirth);
    }

    [Fact]
    public void TheSlotListReadsFourCharacterRecordsWithEmptySlots()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)CharacterSealResult.Succeeded);
        WriteSlot(p, "Zeus", 1, 101, 60);
        WriteSlot(p, "", 0, 0, 0);
        WriteSlot(p, "Velkan", 1, 105, 70);
        WriteSlot(p, "", 0, 0, 0);
        p.ResetOffset();

        var list = CharacterSealWire.ReadList(p);

        Assert.True(list.Ok);
        Assert.Equal(["Zeus", "", "Velkan", ""], list.Slots.Select(s => s.Name));
        Assert.Equal(70, list.Slots[2].Level);
        Assert.Equal(105, list.Slots[2].Class);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ARefusedListCarriesItsCode()
    {
        var list = CharacterSealWire.ReadList(Reply((byte)CharacterSealResult.Failed, (byte)CharacterSeal.CodeTooSoon));
        Assert.False(list.Ok);
        Assert.Equal(CharacterSeal.CodeTooSoon, list.Code);
    }

    [Fact]
    public void ASealedScrollBecomesARingWithItsSummary()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)CharacterSealResult.Succeeded);
        p.WriteByte(3);
        p.WriteInt(Ring);
        p.WriteInt(11);
        p.WriteString("Velkan");
        WriteSummary(p, "Velkan", 105, 70, 2500, 1, 2);
        p.ResetOffset();

        var outcome = CharacterSealWire.ReadSeal(p);

        Assert.Equal(3, outcome.BagPos);
        Assert.Equal(Ring, outcome.ItemId);
        Assert.Equal(11, outcome.UniqueId);
        Assert.Equal(new PetItemInfo(11, "Velkan", 105, 70, 2500, 1, 2), outcome.Summary);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void SealRefusalsReadTheirCodeInBothShapes()
    {
        var plain = CharacterSealWire.ReadSeal(Reply((byte)CharacterSealResult.Failed, (byte)CharacterSeal.CodeWrongVaultPassword));
        Assert.Equal(CharacterSeal.WrongVaultPasswordText, CharacterSeal.SealRefusalText(plain.Result, plain.Code));

        var extended = CharacterSealWire.ReadSeal(Reply((byte)CharacterSealResult.Extended, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (short)CharacterSeal.RefusalBoundGear));
        Assert.Equal(CharacterSeal.BoundGearText, CharacterSeal.SealRefusalText(extended.Result, extended.Code));

        Assert.Equal(CharacterSeal.SealFailedText, CharacterSeal.SealRefusalText(CharacterSealResult.Failed, 1));
        Assert.Equal(CharacterSeal.VoteWarningText, CharacterSeal.SealRefusalText(CharacterSealResult.Failed, CharacterSeal.CodeJuraidVotes));
        Assert.Equal(CharacterSeal.SealFailedText, CharacterSeal.SealRefusalText(CharacterSealResult.Extended, -9));
    }

    [Fact]
    public void AnUnsealNamesTheBagPositionToEmpty()
    {
        Assert.Equal(new CharacterUnsealOutcome(CharacterSealResult.Succeeded, 3), CharacterSealWire.ReadUnseal(Reply((byte)CharacterSealResult.Succeeded, (byte)3, 11)));
        Assert.Equal(new CharacterUnsealOutcome(CharacterSealResult.Failed, -1), CharacterSealWire.ReadUnseal(Reply((byte)CharacterSealResult.Failed, (byte)0)));
    }

    [Fact]
    public void TheRingSheetReadsStatsSkillsAndFortyTwoItems()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)CharacterSealResult.Succeeded);
        p.WriteSByteString("Velkan");
        p.WriteByte(1);
        p.WriteByte(1);
        p.WriteUShort(105);
        p.WriteByte(70);
        p.WriteInt(1200);
        foreach (byte stat in new byte[] { 90, 80, 70, 60, 50 }) p.WriteByte(stat);
        p.WriteInt(5_000_000);
        p.WriteByte(4);
        p.WriteInt(1);
        foreach (byte tree in new byte[] { 20, 30, 40, 2 }) p.WriteByte(tree);
        for (int i = 0; i < CharacterSealWire.SheetItems; i++)
        {
            p.WriteInt(i == 6 ? 120010000 : 0);
            p.WriteUShort(i == 6 ? (ushort)5000 : (ushort)0);
            p.WriteUShort(i == 6 ? (ushort)1 : (ushort)0);
            p.WriteByte(0);
        }
        for (int i = 0; i < 6; i++) p.WriteByte(0);
        p.ResetOffset();

        var sheet = CharacterSealWire.ReadSheet(p)!;

        Assert.Equal("Velkan", sheet.Name);
        Assert.Equal(105, sheet.Class);
        Assert.Equal(1200, sheet.Loyalty);
        Assert.Equal([90, 80, 70, 60, 50], sheet.Stats);
        Assert.Equal(5_000_000, sheet.Gold);
        Assert.Equal(4, sheet.FreePoints);
        Assert.Equal([20, 30, 40, 2], sheet.SkillTrees);
        Assert.Equal(120010000, sheet.Items[6].ItemId);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ARefusedSheetReadsAsNothing() =>
        Assert.Null(CharacterSealWire.ReadSheet(Reply((byte)CharacterSealResult.Extended)));

    [Fact]
    public void AchievementIdsAreCountedByBytes()
    {
        var p = Reply((byte)CharacterSealResult.Succeeded, 4, (ushort)101, (ushort)202);
        Assert.Equal([101, 202], CharacterSealWire.ReadAchievements(p));
        Assert.Empty(CharacterSealWire.ReadAchievements(Reply((byte)CharacterSealResult.Succeeded, 0)));
    }

    [Fact]
    public void ATradedRingCarriesItsUniqueIdAndSummary()
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteInt(11);
        WriteSummary(p, "Velkan", 105, 70, 2500, 1, 0);
        p.WriteInt(0);
        p.ResetOffset();

        Assert.Equal(11, PetWire.ReadUniqueId(p, out var summary));
        Assert.Equal("Velkan", summary!.Value.Name);
        Assert.Equal(0, PetWire.ReadUniqueId(p, out var none));
        Assert.Null(none);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void SealPicksOtherCharactersAndUnsealPicksEmptySlots()
    {
        var other = new CharacterSummary { Name = "Velkan" };
        var self = new CharacterSummary { Name = "Zeus" };
        var empty = new CharacterSummary();
        Assert.True(CharacterSeal.SlotPickable(CharacterSeal.Mode.Seal, other, "zeus"));
        Assert.False(CharacterSeal.SlotPickable(CharacterSeal.Mode.Seal, self, "zeus"));
        Assert.False(CharacterSeal.SlotPickable(CharacterSeal.Mode.Seal, empty, "zeus"));
        Assert.True(CharacterSeal.SlotPickable(CharacterSeal.Mode.Unseal, empty, "zeus"));
        Assert.False(CharacterSeal.SlotPickable(CharacterSeal.Mode.Unseal, other, "zeus"));
        Assert.True(CharacterSeal.Fits(CharacterSeal.Mode.Seal, CharacterSeal.ScrollKind));
        Assert.False(CharacterSeal.Fits(CharacterSeal.Mode.Unseal, CharacterSeal.ScrollKind));
    }

    [Fact]
    public void TheHelpTextSplitsIntoTheSealAndUnsealPanels()
    {
        const string help = "Character Seal\n\nUsing Character Seal Scroll, you can seal one of your character into Cypher Ring\n"
            + "Character Seal Scroll Item required\n\nCharacter Unseal\n\nYou can unseal the character in Cypher Ring into your empty character slot";
        Assert.Equal(("Character Seal", "Using Character Seal Scroll, you can seal one of your character into Cypher Ring\nCharacter Seal Scroll Item required"),
            CharacterSeal.HelpPanel(help, CharacterSeal.Mode.Seal));
        Assert.Equal(("Character Unseal", "You can unseal the character in Cypher Ring into your empty character slot"),
            CharacterSeal.HelpPanel(help, CharacterSeal.Mode.Unseal));
        Assert.Equal("90+5", CharacterSeal.StatLine(90, 5));
        Assert.Equal("90", CharacterSeal.StatLine(90, 0));
    }
}
