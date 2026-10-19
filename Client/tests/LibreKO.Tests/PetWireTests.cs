using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
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

    private static Packet Reply(byte[] data)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE); p.WriteBytes(data); p.ResetOffset();
        return p;
    }

    [Fact]
    public void AHatchReplyForTheRequestIsAcceptedAndAnyOtherReplyFailsIt()
    {
        Assert.True(PetWire.TryReadHatchFor(Reply(HatchReply()), PetIncubationRequest.Hatch(Kaul, 4), out var hatched, out int failure));
        Assert.Equal(4, hatched.BagSlot); Assert.Equal(0, failure);

        Assert.False(PetWire.TryReadHatchFor(Reply(HatchReply()), PetIncubationRequest.Hatch(Kaul, 5), out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadHatchFor(Reply(HatchReply()[..^1]), PetIncubationRequest.Hatch(Kaul, 4), out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadHatchFor(Reply(new byte[] { PetWire.HatchRefused, 3 }), PetIncubationRequest.Hatch(Kaul, 4), out _, out failure));
        Assert.Equal(3, failure);
    }

    private const string NewName = "Brownie";
    private const int CertificateSlot = 7;

    private static byte[] RenameReply(string name = NewName)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetWire.RenameSucceeded); p.WriteInt(Kaul);
        p.WriteByte(4); p.WriteInt(9); p.WriteString(name);
        p.WriteByte(101); p.WriteByte(12); p.WriteUShort(4200); p.WriteShort(7300);
        p.WriteByte(0); p.WriteInt(PetWire.NameCertificate); p.WriteByte(CertificateSlot);
        return p.GetData();
    }

    private static PetRenameRequest RenameRequest() => new(Kaul, 4, 9, PetWire.NameCertificate, CertificateSlot, NewName);

    [Fact]
    public void ARenamedFamiliarComesBackWithTheSpentCertificate()
    {
        var p = Reply(RenameReply());

        Assert.True(PetWire.TryReadRename(p, out var renamed, out int failure));
        Assert.Equal(0, failure);
        Assert.Equal(Kaul, renamed.Pet.ItemId);
        Assert.Equal(4, renamed.Pet.BagSlot);
        Assert.Equal(9, renamed.Pet.Info.Index);
        Assert.Equal(NewName, renamed.Pet.Info.Name);
        Assert.Equal(12, renamed.Pet.Info.Level);
        Assert.Equal(7300, renamed.Pet.Info.Satisfaction);
        Assert.Equal(PetWire.NameCertificate, renamed.CertificateItemId);
        Assert.Equal(CertificateSlot, renamed.CertificateSlot);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ARefusedRenameSaysWhy(byte code)
    {
        Assert.False(PetWire.TryReadRename(Reply(new[] { PetWire.RenameRefused, code }), out var renamed, out int failure));
        Assert.Equal(default, renamed);
        Assert.Equal(code, failure);
    }

    [Fact]
    public void EveryTruncatedRenameIsIgnored()
    {
        byte[] data = RenameReply();
        for (int length = 0; length < data.Length; length++)
        {
            Assert.False(PetWire.TryReadRename(Reply(data[..length]), out var renamed, out int failure));
            Assert.Equal(default, renamed);
            Assert.Equal(PetWire.MalformedReplyCode, failure);
        }
    }

    [Fact]
    public void ARenameReplyForTheRequestIsAcceptedAndAnyOtherReplyFailsIt()
    {
        Assert.True(PetWire.TryReadRenameFor(Reply(RenameReply()), RenameRequest(), out _, out int failure));
        Assert.Equal(0, failure);

        Assert.False(PetWire.TryReadRenameFor(Reply(RenameReply("Other")), RenameRequest(), out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadRenameFor(Reply(RenameReply()), RenameRequest() with { UniqueId = 10 }, out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadRenameFor(Reply(RenameReply()), RenameRequest() with { CertificateSlot = 8 }, out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
    }

    [Fact]
    public void ARenameRenamesTheFamiliarAndTakesTheCertificateOutOfTheBag()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoSink(), session, new LoginState()), () => 0);
        int familiar = InventoryConstants.InventoryStart + 4;
        int certificate = InventoryConstants.InventoryStart + CertificateSlot;
        session.Self.ApplyInventorySlot(familiar, new ItemSlot { ItemId = Kaul, Count = 1, Durability = 1, UniqueId = 9 });
        session.Self.ApplyInventorySlot(certificate, new ItemSlot { ItemId = PetWire.NameCertificate, Count = 1, Durability = 1 });
        session.Pet.Remember(new PetItemInfo(9, Name, 101, 12, 4200, 7300));
        session.Pet.PendingRename = RenameRequest();
        PetItemInfo? renamed = null;
        session.Pet.PetRenamedEvent += (_, info) => renamed = info;

        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte((byte)PetSubOpcode.ModeFunction);
        p.WriteByte((byte)PetFunction.NameChange);
        p.WriteBytes(RenameReply());
        p.ResetOffset();
        dispatcher.Dispatch(p);

        Assert.Equal(NewName, renamed?.Name);
        Assert.Equal(NewName, session.Pet.Items[9].Name);
        Assert.Equal(9, session.Self.Inventory[familiar].UniqueId);
        Assert.True(session.Self.Inventory[certificate].IsEmpty);
        Assert.Null(session.Pet.PendingRename);
    }

    private sealed class NoSink : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    [Fact]
    public void ATransformReplyForTheRequestIsAcceptedAndAnyOtherReplyFailsIt()
    {
        var request = PetIncubationRequest.Transformation(Kaul, 4, 9, EtarothScroll, 7);
        Assert.True(PetWire.TryReadTransformFor(Reply(HatchReply(true)), request, out var transformed, out int failure));
        Assert.Equal(Etaroth, transformed.Pet.ItemId); Assert.Equal(0, failure);

        Assert.False(PetWire.TryReadTransformFor(Reply(HatchReply(true)), request with { UniqueId = 10 }, out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadTransformFor(Reply(HatchReply(true)[..^1]), request, out _, out failure));
        Assert.Equal(PetWire.MalformedReplyCode, failure);
        Assert.False(PetWire.TryReadTransformFor(Reply(new byte[] { PetWire.HatchNameTaken }), request, out _, out failure));
        Assert.Equal(PetWire.NameTakenCode, failure);
    }
}
