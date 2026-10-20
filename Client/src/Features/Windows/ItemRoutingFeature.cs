using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Windows;

internal sealed class ItemRoutingFeature : IFeature, IItemActions
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly SelfSession self;

    public ItemRoutingFeature(IWorldContext context, WorldRoot world, SelfSession self)
    {
        this.context = context;
        this.world = world;
        this.self = self;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    public void Context(int absSlot)
    {
        if (absSlot < 0 || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        if (Input.IsKeyPressed(Key.Shift)
            && (context.Chat.InsertItemLink(self.Inventory[absSlot].ItemId) || world.ChatRoom.InsertItemLink(self.Inventory[absSlot].ItemId)
                || world.Whispers.InsertWhisperItemLink(self.Inventory[absSlot].ItemId))) return;
        if (context.BagCompanions.Current != null && context.BagCompanions.Current.Take(absSlot)) return;
        if (world.Exchange.IsOpen && ItemMove.RegionOf(absSlot) == ItemMove.Region.Grid)
        {
            world.Exchange.OfferSlot(absSlot);
            return;
        }
        if (world.MerchantBuy.WishShown && ItemMove.RegionOf(absSlot) is ItemMove.Region.Grid or ItemMove.Region.MagicBag)
        {
            world.MerchantBuy.WishFromBag(absSlot, -1);
            return;
        }
        var def = ItemData.Get(self.Inventory[absSlot].ItemId);
        if (def == null) return;
        if (world.CharacterSealWindow.ShowSealedSheet(self.Inventory[absSlot])) return;
        if (world.SlaveMerchantWindow.AskRedeem(absSlot)) return;

        if (ItemMove.RegionOf(absSlot) != ItemMove.Region.Grid
            || ItemData.EquipSlotFor(def) >= 0
            || def.Slot >= InventoryFeature.CospreCodeBase
            || def.Slot == InventoryFeature.ItemSlotCodeBag)
        {
            world.InventoryWindow.Activate(absSlot);
            return;
        }
        if (self.Inventory[absSlot].ItemId == ExpSeal.EmptyJar)
        {
            world.ExpSealWindow.Open(absSlot);
            return;
        }
        if (VipWarehouseFeature.IsVaultKey(self.Inventory[absSlot].ItemId))
        {
            world.VipWarehouse.AskKey(absSlot);
            return;
        }
        if (context.Zone == FullMoon.Zone && def.Effect1 != 0 && SkillData.Get(def.Effect1) is { Tree: FullMoon.SkillTree })
        {
            world.SkillCastSystem.CastSkill(def.Effect1);
            return;
        }
        if (def.Effect1 != 0 && SkillData.IsSkill(def.Effect1))
            world.Hotbar.AddTo(self.Inventory[absSlot].ItemId);
    }

    public void OpenVault() => world.VipWarehouse.OpenVaultFromBag();

    public void OpenStore() => world.ShoppingMall.Toggle();
}
