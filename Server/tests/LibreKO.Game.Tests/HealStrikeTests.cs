using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class HealStrikeTests
{
    private const int MaceId = 1111471741;
    private const int PlainId = 1111471000;
    private const long Now = 1_800_000_000;

    private static IEnumerable<ItemOpData>? Ops(int itemId) => itemId == MaceId
        ? [new ItemOpData { ItemId = MaceId, TriggerType = HealStrike.ItemOpOnEquip, SkillId = HealStrike.ArmingSkill, TriggerRate = 100 }]
        : null;

    private static ItemSlot[] Inventory(int slot, int itemId)
    {
        var inventory = new ItemSlot[InventoryConstants.SlotMax + InventoryConstants.HaveMax];
        for (var i = 0; i < inventory.Length; i++)
            inventory[i] = new ItemSlot();
        inventory[slot].ItemId = itemId;
        return inventory;
    }

    [Fact]
    public void AWornMaceArmsTheStrikeButOneInTheBagDoesNot()
    {
        HealStrike.Armed(Inventory(InventoryConstants.RightHand, MaceId), Ops, Now).Should().BeTrue();
        HealStrike.Armed(Inventory(InventoryConstants.SlotMax, MaceId), Ops, Now).Should().BeFalse();
        HealStrike.Armed(Inventory(InventoryConstants.RightHand, PlainId), Ops, Now).Should().BeFalse();
    }

    [Fact]
    public void AWornMaceThatRanOutDoesNotArmTheStrike()
    {
        var inventory = Inventory(InventoryConstants.RightHand, MaceId);
        inventory[InventoryConstants.RightHand].ExpiresAt = Now;

        HealStrike.Armed(inventory, Ops, Now).Should().BeFalse();
        HealStrike.Armed(inventory, Ops, Now - 1).Should().BeTrue();
    }

    [Fact]
    public void OnlyAnArmedHealthHealOnAFriendTargetTurns()
    {
        HealStrike.Turns(true, SkillMoral.FriendWithMe, MagicDirectType.Health).Should().BeTrue();
        HealStrike.Turns(false, SkillMoral.FriendWithMe, MagicDirectType.Health).Should().BeFalse();
        HealStrike.Turns(true, SkillMoral.Party, MagicDirectType.Health).Should().BeFalse();
        HealStrike.Turns(true, SkillMoral.FriendWithMe, MagicDirectType.Mana).Should().BeFalse();
    }

    [Fact]
    public void TheStrikeIsHalfTheHeal()
    {
        HealStrike.Damage(720).Should().Be(360);
        HealStrike.Damage(0).Should().Be(0);
        HealStrike.Damage(-24).Should().Be(0);
    }

    [Fact]
    public void TheStatBlockCarriesTheFlagRightAfterTheMaxWeight()
    {
        var writer = ItemMovePacketWriter.Move((byte)ItemMoveSubOpcode.Move);
        writer.Stats = new ItemMovePacketWriter.StatBlock { MaxWeight = 5000, HealStrikesEnemies = true };
        var packet = writer.Build();
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadInt().Should().Be(5000);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(0);
    }
}
