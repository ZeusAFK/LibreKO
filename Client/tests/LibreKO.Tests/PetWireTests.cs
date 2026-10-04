using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PetWireTests
{
    private const int Kaul = 610001000;
    private const string Name = "Kauly";
    private const int Etaroth = 610015000;
    private const int EtarothScroll = 700019001;

    private static Packet ItemRecord(int itemId, int uniqueId)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteInt(itemId);
        p.WriteShort(1);
        p.WriteShort(1);
        p.WriteByte(0);
        p.WriteShort(0);
        p.WriteInt(uniqueId);
        if (uniqueId != 0)
        {
            p.WriteString(Name);
            p.WriteByte(5);
            p.WriteByte(3);
            p.WriteUShort(1234);
            p.WriteShort(9000);
            p.WriteByte(0);
        }
        p.WriteInt(0);
        p.WriteInt(Kaul + 1);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AnItemWithAUniqueIdCarriesItsFamiliar()
    {
        var p = ItemRecord(Kaul, 42);

        var slot = PetWire.ReadItemRecord(p, out var pet);

        Assert.Equal(Kaul, slot.ItemId);
        Assert.Equal(42, slot.UniqueId);
        Assert.NotNull(pet);
        Assert.Equal(Name, pet!.Value.Name);
        Assert.Equal(3, pet.Value.Level);
        Assert.Equal(1234, pet.Value.ExpPercent);
        Assert.Equal(9000, pet.Value.Satisfaction);
        Assert.Equal(Kaul + 1, p.ReadInt());
    }

    [Fact]
    public void APlainItemRecordIsNineteenBytes()
    {
        var p = ItemRecord(Kaul, 0);

        PetWire.ReadItemRecord(p, out var pet);

        Assert.Null(pet);
        Assert.Equal(Kaul + 1, p.ReadInt());
    }

    [Fact]
    public void TheSummonSheetReadsTheFamiliarAndItsFourBagSlots()
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteInt(7);
        p.WriteString(Name);
        p.WriteByte(101);
        p.WriteByte(12);
        p.WriteUShort(4375);
        p.WriteShort(168);
        p.WriteShort(131);
        p.WriteShort(190);
        p.WriteShort(185);
        p.WriteShort(7240);
        p.WriteShort(51);
        p.WriteShort(110);
        for (int i = 0; i < PetSheet.ResistanceCount; i++) p.WriteByte(4);
        for (int i = 0; i < PetSheet.InventorySize; i++)
        {
            p.WriteInt(i == 1 ? 700012000 : 0);
            p.WriteShort(0); p.WriteShort(i == 1 ? (short)1 : (short)0); p.WriteByte(0);
            p.WriteShort(0); p.WriteInt(0); p.WriteInt(0);
        }
        p.ResetOffset();

        var sheet = PetWire.ReadSheet(p);

        Assert.Equal(7, sheet.Index);
        Assert.Equal(Name, sheet.Name);
        Assert.Equal(12, sheet.Level);
        Assert.Equal(131, sheet.Hp);
        Assert.Equal(185, sheet.Mp);
        Assert.Equal(7240, sheet.Satisfaction);
        Assert.Equal(4, sheet.Resists[5]);
        Assert.Equal(700012000, sheet.Items[1].ItemId);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AHatchedEggComesBackAsTheFamiliarsItem()
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetWire.HatchSucceeded);
        p.WriteInt(Kaul);
        p.WriteByte(4);
        p.WriteInt(9);
        p.WriteString(Name);
        p.WriteByte(101);
        p.WriteByte(1);
        p.WriteUShort(0);
        p.WriteShort(9000);
        p.ResetOffset();

        Assert.True(PetWire.TryReadHatch(p, out var hatched, out _));
        Assert.Equal(Kaul, hatched.ItemId);
        Assert.Equal(4, hatched.BagSlot);
        Assert.Equal(9, hatched.Info.Index);
        Assert.Equal(Name, hatched.Info.Name);
        Assert.Equal(9000, hatched.Info.Satisfaction);
    }

    [Fact]
    public void ATransformedFamiliarComesBackWithTheSpentScroll()
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetWire.HatchSucceeded);
        p.WriteInt(Etaroth);
        p.WriteByte(4);
        p.WriteInt(9);
        p.WriteString(Name);
        p.WriteByte(115);
        p.WriteByte(12);
        p.WriteUShort(4200);
        p.WriteShort(7300);
        p.WriteByte(0);
        p.WriteInt(EtarothScroll);
        p.WriteByte(7);
        p.ResetOffset();

        Assert.True(PetWire.TryReadTransform(p, out var transformed, out _));
        Assert.Equal(Etaroth, transformed.Pet.ItemId);
        Assert.Equal(4, transformed.Pet.BagSlot);
        Assert.Equal(9, transformed.Pet.Info.Index);
        Assert.Equal(12, transformed.Pet.Info.Level);
        Assert.Equal(EtarothScroll, transformed.MaterialItemId);
        Assert.Equal(7, transformed.MaterialSlot);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Theory]
    [InlineData(PetWire.HatchNameTaken, 0, PetWire.NameTakenCode)]
    [InlineData(0, 2, 2)]
    public void ARefusedHatchSaysWhy(byte result, byte code, int expected)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(result);
        p.WriteByte(code);
        p.ResetOffset();

        Assert.False(PetWire.TryReadHatch(p, out _, out int failure));
        Assert.Equal(expected, failure);
    }
}
