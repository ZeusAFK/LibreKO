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

    private static byte[] HatchReply(bool transform = false)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(PetWire.HatchSucceeded); p.WriteInt(transform ? Etaroth : Kaul);
        p.WriteByte(4); p.WriteInt(9); p.WriteString(Name);
        p.WriteByte(101); p.WriteByte(12); p.WriteUShort(4200); p.WriteShort(7300);
        if (transform) { p.WriteByte(0); p.WriteInt(EtarothScroll); p.WriteByte(7); }
        return p.GetData();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTruncatedSuccessIsIgnoredWithoutAnInventoryResult(bool transform)
    {
        byte[] data = HatchReply(transform);
        for (int length = 0; length < data.Length; length++)
        {
            var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE); p.WriteBytes(data[..length]);
            int failure;
            if (transform)
            {
                Assert.False(PetWire.TryReadTransform(p, out var result, out failure));
                Assert.Equal(default, result);
            }
            else
            {
                Assert.False(PetWire.TryReadHatch(p, out var result, out failure));
                Assert.Equal(default, result);
            }
            Assert.Equal(PetWire.MalformedReplyCode, failure);
        }
    }

    [Theory]
    [InlineData(10, 255)]
    [InlineData(10, 127)]
    [InlineData(10, 0)]
    [InlineData(5, InventoryConstants.HaveMax)]
    [InlineData(6, 0)]
    [InlineData(1, 0)]
    public void InvalidNameLengthsAndItemAddressesAreNotAccepted(int offset, byte value)
    {
        byte[] data = HatchReply();
        if (offset is 1 or 6) Array.Clear(data, offset, 4);
        else data[offset] = value;
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE); p.WriteBytes(data);
        Assert.False(PetWire.TryReadHatch(p, out var result, out int failure));
        Assert.Equal(default, result); Assert.Equal(PetWire.MalformedReplyCode, failure);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(InventoryConstants.HaveMax)]
    public void TransformCannotConsumeTheFamiliarOrAnOutsideInventorySlot(byte slot)
    {
        byte[] data = HatchReply(true); data[^1] = slot;
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE); p.WriteBytes(data);
        Assert.False(PetWire.TryReadTransform(p, out var result, out int failure));
        Assert.Equal(default, result); Assert.Equal(PetWire.MalformedReplyCode, failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(255)]
    public void IncompleteRefusalOrUnknownResultDoesNotCompleteAnOperation(byte resultCode)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE); p.WriteByte(resultCode);
        Assert.False(PetWire.TryReadHatch(p, out _, out int failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
    }

    private static TransformedPet Transformed(int itemId = Etaroth, int slot = 4, int index = 9, int scroll = EtarothScroll, int scrollSlot = 7) =>
        new(new HatchedPet(itemId, slot, new PetItemInfo(index, Name, 101, 12, 0, 0)), scroll, scrollSlot);

    [Fact]
    public void AHatchResultOnlyAnswersAHatchForTheSameBagSlot()
    {
        var request = PetIncubationRequest.Hatch(Kaul, 4);
        Assert.True(request.Matches(new HatchedPet(Kaul, 4, new PetItemInfo(9, Name, 101, 1, 0, 0))));
        Assert.False(request.Matches(new HatchedPet(Kaul, 5, new PetItemInfo(9, Name, 101, 1, 0, 0))));
        Assert.False(request.Matches(Transformed()));
    }

    [Fact]
    public void ATransformResultMustNameTheSameFamiliarScrollAndSlots()
    {
        var request = PetIncubationRequest.Transformation(Kaul, 4, 9, EtarothScroll, 7);
        Assert.True(request.Matches(Transformed()));
        Assert.False(request.Matches(Transformed(itemId: Kaul)));
        Assert.False(request.Matches(Transformed(slot: 5)));
        Assert.False(request.Matches(Transformed(index: 10)));
        Assert.False(request.Matches(Transformed(scroll: EtarothScroll + 1)));
        Assert.False(request.Matches(Transformed(scrollSlot: 8)));
        Assert.False(request.Matches(Transformed().Pet));
        Assert.True(PetIncubationRequest.Transformation(Kaul, 4, PetIncubationRequest.AnyFamiliar, EtarothScroll, 7)
            .Matches(Transformed(index: 10)));
    }
}
