using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class VipVaultTests : GameTestBase
{
    private const string Pin = "1234";
    private const string WrongPin = "0000";
    private const int VaultKey = 800_442_000;
    private const int StoredItem = 156_210_000;
    private const byte NonStorableRace = 73;
    private const short AliveHp = 100;
    private const int ItemRecordBytes = 17;
    private const int StorageRecordBytes = 5;

    [Fact]
    public async Task EachWrongPinIsCountedAndTheThirdLocksTheVault()
    {
        var vault = Vault(_ => { });
        vault.Session.VipPassword = Pin;
        vault.Session.VipPasswordRequest = 1;

        var counts = new List<byte>();
        for (var i = 0; i < VipWarehousePacketCoordinator.PasswordTriesAllowed; i++)
        {
            await vault.EnterPin(WrongPin);
            counts.Add(vault.LastPinRejection());
        }
        await vault.EnterPin(Pin);

        counts.Should().Equal(1, 2, 3);
        vault.LastPinRejection().Should().Be(VipWarehousePacketCoordinator.PasswordTriesAllowed);
        vault.Session.VipPasswordRequest.Should().Be(1);
    }

    [Fact]
    public async Task TheRightPinClearsTheCount()
    {
        var vault = Vault(_ => { });
        vault.Session.VipPassword = Pin;
        vault.Session.VipPasswordRequest = 1;

        await vault.EnterPin(WrongPin);
        await vault.EnterPin(Pin);

        vault.Session.VipPasswordFailures.Should().Be(0);
        vault.Session.VipPasswordRequest.Should().Be(0);
    }

    [Fact]
    public async Task ALockedVaultHandsNothingOutAndKeepsItsPin()
    {
        var vault = Vault(_ => { });
        vault.Session.VipPassword = Pin;
        vault.Session.VipPasswordRequest = 1;
        vault.Session.VipWarehouse[0].ItemId = StoredItem;
        vault.Session.VipWarehouse[0].Count = 1;

        await vault.Send(VipWarehouseSubOpcode.Output, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });
        await vault.Send(VipWarehouseSubOpcode.SetPassword, packet => packet.WriteSByteString(WrongPin));

        vault.Session.VipWarehouse[0].ItemId.Should().Be(StoredItem);
        vault.Session.VipPassword.Should().Be(Pin);
        vault.LastResult(VipWarehouseSubOpcode.SetPassword).Should().Be((byte)VipWarehouseResult.Rejected);
    }

    [Fact]
    public async Task AKeyIsRefusedWhileMoreThanSixtyDaysAreLeft()
    {
        var vault = Vault(_ => { });
        vault.Session.VipVaultExpiry = DateTime.UtcNow.AddDays(61);
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId = VaultKey;
        vault.Session.Inventory[InventoryConstants.SlotMax].Count = 1;

        await vault.UseKey(VaultKey, 0);

        vault.LastResult(VipWarehouseSubOpcode.UseVault).Should().Be((byte)VipWarehouseResult.RentalAtCap);
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(VaultKey);
    }

    [Fact]
    public async Task AnItemThatIsNotAVaultKeyIsNamedAsSuch()
    {
        var vault = Vault(_ => { });

        await vault.UseKey(StoredItem, 0);

        vault.LastResult(VipWarehouseSubOpcode.UseVault).Should().Be((byte)VipWarehouseResult.NotAVaultKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAPlayerNotSettingUpAStallStoresAnItem(bool preparing)
    {
        var vault = Vault(gameData => gameData.GetItem(StoredItem)
            .Returns(new ItemData { Num = StoredItem, Race = 1, Countable = 0, Duration = 50 }));
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId = StoredItem;
        vault.Session.Inventory[InventoryConstants.SlotMax].Count = 1;
        vault.Session.Inventory[InventoryConstants.SlotMax].Durability = 50;
        vault.Session.Trade.IsSellingMerchantPreparing = preparing;

        await vault.Send(VipWarehouseSubOpcode.Input, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        vault.LastResult(VipWarehouseSubOpcode.Input).Should().Be((byte)(preparing ? VipWarehouseResult.Failed : VipWarehouseResult.Succeeded));
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(preparing ? StoredItem : 0);
        vault.Session.VipWarehouse[0].ItemId.Should().Be(preparing ? 0 : StoredItem);
    }

    [Fact]
    public async Task ANonStorableItemStaysInTheBag()
    {
        var vault = Vault(gameData => gameData.GetItem(StoredItem)
            .Returns(new ItemData { Num = StoredItem, Race = NonStorableRace, Countable = 0, Duration = 50 }));
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId = StoredItem;
        vault.Session.Inventory[InventoryConstants.SlotMax].Count = 1;

        await vault.Send(VipWarehouseSubOpcode.Input, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        vault.LastResult(VipWarehouseSubOpcode.Input).Should().Be((byte)VipWarehouseResult.Failed);
        vault.Session.VipWarehouse[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AKeyFromItsBagSlotExtendsTheOwnStorageAndLeavesTheBag()
    {
        var vault = Vault(_ => { });
        vault.Session.VipVaultExpiry = DateTime.UtcNow.AddDays(-1);
        vault.Session.VipVaultExpiryAnnounced = true;
        vault.Session.Inventory[InventoryConstants.SlotMax + 2].ItemId = VipVault.InnVaultKey;
        vault.Session.Inventory[InventoryConstants.SlotMax + 2].Count = 1;

        await vault.UseKey(VipVault.InnVaultKey, 2);

        var reply = vault.Last(VipWarehouseSubOpcode.UseVault);
        reply.ReadByte().Should().Be((byte)VipWarehouseResult.Succeeded);
        reply.ReadByte().Should().Be(VipVault.OwnStorage);
        reply.ReadUInt().Should().Be(VipVault.ExpiresAt(vault.Session.VipVaultExpiry));
        vault.Session.VipVaultExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        vault.Session.VipVaultExpiryAnnounced.Should().BeFalse();
        vault.Session.Inventory[InventoryConstants.SlotMax + 2].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AKeyNamedAtAnotherBagSlotIsRefused()
    {
        var vault = Vault(_ => { });
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId = VaultKey;
        vault.Session.Inventory[InventoryConstants.SlotMax].Count = 1;

        await vault.UseKey(VaultKey, 5);

        vault.LastResult(VipWarehouseSubOpcode.UseVault).Should().Be((byte)VipWarehouseResult.Rejected);
        vault.Session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(VaultKey);
    }

    [Fact]
    public async Task AnExpiredVaultOpensWithItsContentsAndThePinFlag()
    {
        var vault = Vault(_ => { });
        var expiry = DateTime.UtcNow.AddDays(-2);
        vault.Session.VipVaultExpiry = expiry;
        vault.Session.VipPassword = Pin;
        vault.Session.VipWarehouse[3].ItemId = StoredItem;
        vault.Session.VipWarehouse[3].Count = 1;

        await vault.Send(VipWarehouseSubOpcode.Open, _ => { });

        var reply = vault.Last(VipWarehouseSubOpcode.Open);
        reply.ReadByte().Should().Be((byte)VipWarehouseResult.Succeeded);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be(0);
        reply.ReadUInt().Should().Be(VipVault.ExpiresAt(expiry));
        for (var storage = 1; storage < VipVault.Storages; storage++)
        {
            reply.ReadByte().Should().Be(0);
            reply.ReadUInt().Should().Be(0u);
        }
        Skip(reply, 3 * ItemRecordBytes);
        reply.ReadInt().Should().Be(StoredItem);
        Skip(reply, ItemRecordBytes - sizeof(int) + (UserSession.VipWarehouseMax - 4) * ItemRecordBytes);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AnExpiredVaultHandsNothingOut()
    {
        var vault = Vault(_ => { });
        vault.Session.VipVaultExpiry = DateTime.UtcNow.AddMinutes(-1);
        vault.Session.VipWarehouse[0].ItemId = StoredItem;
        vault.Session.VipWarehouse[0].Count = 1;

        await vault.Send(VipWarehouseSubOpcode.Output, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        vault.LastResult(VipWarehouseSubOpcode.Output).Should().Be((byte)VipWarehouseResult.Expired);
        vault.Session.VipWarehouse[0].ItemId.Should().Be(StoredItem);
    }

    [Fact]
    public async Task TheEndOfTheRentalIsAnnouncedOnce()
    {
        var vault = Vault(_ => { });
        vault.Session.VipVaultExpiry = DateTime.UtcNow.AddMinutes(-1);
        var expiry = new ItemExpiryService(vault.Provider.GetRequiredService<SessionManager>(),
            NullLogger<ItemExpiryService>.Instance);

        await expiry.ProcessTickAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await expiry.ProcessTickAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        vault.Count(VipWarehouseSubOpcode.KeyExpired).Should().Be(1);
        var notice = vault.Last(VipWarehouseSubOpcode.KeyExpired);
        notice.ReadByte().Should().Be(VipVault.OwnStorage);
    }

    [Fact]
    public async Task AVaultNeverRentedIsNotAnnounced()
    {
        var vault = Vault(_ => { });
        vault.Session.VipVaultExpiry = default;
        var expiry = new ItemExpiryService(vault.Provider.GetRequiredService<SessionManager>(),
            NullLogger<ItemExpiryService>.Instance);

        await expiry.ProcessTickAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        vault.Count(VipWarehouseSubOpcode.KeyExpired).Should().Be(0);
    }

    private static void Skip(Packet packet, int bytes)
    {
        for (var i = 0; i < bytes; i++) packet.ReadByte();
    }

    private static VaultHarness Vault(Action<Common.Domain.Services.IGameDataService> data)
    {
        var provider = CreateProvider(_ => { }, data);
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 73, accountId: 83);
        session.Hp = AliveHp;
        session.VipVaultExpiry = DateTime.UtcNow.AddDays(7);
        return new VaultHarness(provider, client, session, sent);
    }

    private sealed record VaultHarness(ServiceProvider Provider, IClient Client, UserSession Session, List<Packet> Sent)
    {
        public Task EnterPin(string pin) =>
            Send(VipWarehouseSubOpcode.EnterPassword, packet => packet.WriteSByteString(pin));

        public Task UseKey(int itemId, byte bagPos) =>
            Send(VipWarehouseSubOpcode.UseVault, packet =>
            {
                packet.WriteInt(0);
                packet.WriteInt(itemId);
                packet.WriteByte(0);
                packet.WriteByte(bagPos);
                packet.WriteByte(VipVault.OwnStorage);
            });

        public Packet Last(VipWarehouseSubOpcode sub)
        {
            var reply = Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_VIP_WAREHOUSE && SubOf(p) == (byte)sub);
            reply.ResetOffset();
            reply.ReadByte();
            return reply;
        }

        public int Count(VipWarehouseSubOpcode sub) =>
            Sent.Count(p => p.GetOpcode() == (byte)GameOpcodes.GS_VIP_WAREHOUSE && SubOf(p) == (byte)sub);

        public async Task Send(VipWarehouseSubOpcode sub, Action<Packet> body)
        {
            var packet = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
            packet.WriteByte((byte)sub);
            body(packet);
            await Provider.GetRequiredService<IVipWarehousePacketCoordinator>().HandleAsync(Client, packet);
        }

        public byte LastResult(VipWarehouseSubOpcode sub)
        {
            var reply = Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_VIP_WAREHOUSE && SubOf(p) == (byte)sub);
            reply.ResetOffset();
            reply.ReadByte();
            return reply.ReadByte();
        }

        public byte LastPinRejection()
        {
            var reply = Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_VIP_WAREHOUSE
                && SubOf(p) == (byte)VipWarehouseSubOpcode.EnterPassword);
            reply.ResetOffset();
            reply.ReadByte();
            reply.ReadByte().Should().Be((byte)VipWarehouseResult.Rejected);
            return reply.ReadByte();
        }

        private static byte SubOf(Packet packet)
        {
            packet.ResetOffset();
            return packet.ReadByte();
        }
    }
}
