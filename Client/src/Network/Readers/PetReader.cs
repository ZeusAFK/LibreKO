namespace LibreKO.Network.Readers;

internal sealed class PetReader : IPacketReader
{
    private readonly ItemsSession items;
    private readonly PetSession pet;
    private readonly SelfSession self;

    public PetReader(ItemsSession items, PetSession pet, SelfSession self)
    {
        this.items = items;
        this.pet = pet;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_PET, HandlePet);
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.PetHatching, HandlePetHatch);
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.PetTransform, HandlePetTransform);
    }

    private void HandlePet(Packet p)
    {
        if (p.RemainingBytes < 2 || (PetSubOpcode)p.ReadByte() != PetSubOpcode.ModeFunction) return;
        switch ((PetFunction)p.ReadByte())
        {
            case PetFunction.Mode: HandlePetMode(p); break;
            case PetFunction.Hp when p.RemainingBytes >= 4 && pet.Pet != null:
                pet.ApplyHp(p.ReadShort(), p.ReadShort());
                break;
            case PetFunction.Mp when p.RemainingBytes >= 4 && pet.Pet != null:
                pet.ApplyMp(p.ReadShort(), p.ReadShort());
                break;
            case PetFunction.TargetHp when p.RemainingBytes >= 15:
                int target = p.ReadInt();
                p.ReadByte();
                p.ReadInt();
                p.ReadInt();
                pet.RaisePetStrike(target, -p.ReadShort());
                break;
            case PetFunction.Exp when p.RemainingBytes >= 13:
                long gained = p.ReadLong();
                int percent = p.ReadUShort();
                int level = p.ReadByte();
                pet.ApplyExp(gained, percent, level, p.ReadShort());
                break;
            case PetFunction.LevelUp when p.RemainingBytes >= 4:
                pet.RaisePetLevelUp(p.ReadInt());
                break;
            case PetFunction.Satisfaction when p.RemainingBytes >= 2:
                pet.ApplySatisfaction(p.ReadShort());
                break;
            case PetFunction.Food:
                HandlePetFood(p);
                break;
        }
    }

    private void HandlePetMode(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        var mode = (PetMode)p.ReadByte();
        var result = (PetModeResult)p.ReadShort();
        if (result != PetModeResult.Succeeded) return;
        switch (mode)
        {
            case PetMode.Summoned:
                pet.ApplySummoned(PetWire.ReadSheet(p));
                break;
            case PetMode.Died:
                pet.ApplyDied();
                break;
            case PetMode.Attack:
            case PetMode.Defence:
            case PetMode.Looting:
                pet.ApplyMode(mode);
                break;
        }
    }

    private void HandlePetFood(Packet p)
    {
        if (pet.PendingFeed is not { } request) return;
        pet.PendingFeed = null;
        var reply = PetWire.ReadFoodFor(p, request);
        if (!reply.Succeeded)
        {
            pet.RaisePetFoodRefused(reply.ItemId);
            return;
        }
        int abs = InventoryConstants.InventoryStart + reply.BagSlot;
        var held = abs < self.Inventory.Length ? self.Inventory[abs] : default;
        if (reply.TryLower(held, out var left))
        {
            self.ApplyInventorySlot(abs, left);
            items.RaiseInventorySlot(abs, left);
        }
        pet.RaisePetFed(reply.BagSlot, reply.ItemId, reply.CountLeft, reply.Increase);
    }

    private void HandlePetHatch(Packet p)
    {
        if (pet.PendingIncubation is not { Transform: false } request) return;
        pet.PendingIncubation = null;
        if (!PetWire.TryReadHatchFor(p, request, out var hatched, out int failure))
        {
            pet.RaisePetHatchFailed(failure);
            return;
        }
        pet.RaisePetHatched(PlaceFamiliarItem(hatched), hatched.Info);
    }

    private void HandlePetTransform(Packet p)
    {
        if (pet.PendingIncubation is not { Transform: true } request) return;
        pet.PendingIncubation = null;
        if (!PetWire.TryReadTransformFor(p, request, out var transformed, out int failure))
        {
            pet.RaisePetTransformFailed(failure);
            return;
        }
        int abs = PlaceFamiliarItem(transformed.Pet);
        SpendBagItem(InventoryConstants.InventoryStart + transformed.MaterialSlot, transformed.MaterialItemId);
        pet.RaisePetTransformed(abs, transformed.Pet.Info);
    }

    private int PlaceFamiliarItem(HatchedPet hatched)
    {
        pet.Remember(hatched.Info);
        int abs = InventoryConstants.InventoryStart + hatched.BagSlot;
        var item = new ItemSlot { ItemId = hatched.ItemId, Count = 1, Durability = 1, UniqueId = hatched.Info.Index };
        self.ApplyInventorySlot(abs, item);
        items.RaiseInventorySlot(abs, item);
        return abs;
    }

    private void SpendBagItem(int abs, int itemId)
    {
        if (!self.Inventory.Holds(abs, itemId)) return;
        var left = Inventory.Consumed(self.Inventory[abs], 1);
        self.ApplyInventorySlot(abs, left);
        items.RaiseInventorySlot(abs, left);
    }
}
