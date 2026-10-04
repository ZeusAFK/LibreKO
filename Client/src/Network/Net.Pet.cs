using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    private const byte PetSubFunction = 1;
    private const byte PetSubSkill = 2;
    private const byte PetFunctionMode = 5;
    private const byte PetFunctionHp = 7;
    private const byte PetFunctionTargetHp = 8;
    private const byte PetFunctionExp = 10;
    private const byte PetFunctionLevelUp = 11;
    private const byte PetFunctionMp = 13;
    private const byte PetFunctionSatisfaction = 15;
    private const byte PetFunctionFood = 16;
    private const short PetResultSucceeded = 1;
    private const byte PetFoodSucceeded = 1;
    private const byte PetFoodTrailFlag = 1;
    private const byte PetFoodTrailPad = 0;
    private const byte PetHatchSub = 6;
    private const byte PetTransformSub = 10;
    private const int PetTransformMaterialSlots = 3;
    public const int PetHatchNameTakenCode = PetWire.NameTakenCode;
    public const int FamiliarSummonSkill = 500117;

    public PetSheet? Pet { get; private set; }
    public readonly Dictionary<int, PetItemInfo> PetItems = new();

    public event Action<PetSheet>? PetSummonedEvent;
    public event Action? PetGoneEvent;
    public event Action<int>? PetModeEvent;
    public event Action? PetVitalsEvent;
    public event Action<long>? PetExpEvent;
    public event Action<int, int, int, int>? PetFedEvent;
    public event Action<int>? PetFoodRefusedEvent;
    public event Action<int, int>? PetStrikeEvent;
    public event Action<int>? PetLevelUpEvent;
    public event Action<int, PetItemInfo>? PetHatchedEvent;
    public event Action<int>? PetHatchFailedEvent;
    public event Action<int, PetItemInfo>? PetTransformedEvent;
    public event Action<int>? PetTransformFailedEvent;

    private void HandlePet(Packet p)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != PetSubFunction) return;
        switch (p.ReadByte())
        {
            case PetFunctionMode: HandlePetMode(p); break;
            case PetFunctionHp when p.RemainingBytes >= 4 && Pet is { } hpPet:
                hpPet.MaxHp = p.ReadShort();
                hpPet.Hp = p.ReadShort();
                PetVitalsEvent?.Invoke();
                break;
            case PetFunctionMp when p.RemainingBytes >= 4 && Pet is { } mpPet:
                mpPet.MaxMp = p.ReadShort();
                mpPet.Mp = p.ReadShort();
                PetVitalsEvent?.Invoke();
                break;
            case PetFunctionTargetHp when p.RemainingBytes >= 15:
                int target = p.ReadInt();
                p.ReadByte();
                p.ReadInt();
                p.ReadInt();
                PetStrikeEvent?.Invoke(target, -p.ReadShort());
                break;
            case PetFunctionExp when p.RemainingBytes >= 13:
                long gained = p.ReadLong();
                int percent = p.ReadUShort();
                int level = p.ReadByte();
                int satisfaction = p.ReadShort();
                if (Pet is { } expPet)
                {
                    expPet.ExpPercent = percent;
                    expPet.Level = level;
                    expPet.Satisfaction = satisfaction;
                    RefreshPetItem(expPet);
                }
                PetExpEvent?.Invoke(gained);
                break;
            case PetFunctionLevelUp when p.RemainingBytes >= 4:
                PetLevelUpEvent?.Invoke(p.ReadInt());
                break;
            case PetFunctionSatisfaction when p.RemainingBytes >= 2:
                int now = p.ReadShort();
                if (Pet is { } satPet)
                {
                    satPet.Satisfaction = now;
                    RefreshPetItem(satPet);
                }
                PetVitalsEvent?.Invoke();
                break;
            case PetFunctionFood when p.RemainingBytes >= 6:
                HandlePetFood(p);
                break;
        }
    }

    private void HandlePetMode(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        int mode = p.ReadByte();
        short result = p.ReadShort();
        if (result != PetResultSucceeded) return;
        switch (mode)
        {
            case PetSheet.ModeSummoned:
                var sheet = PetWire.ReadSheet(p);
                sheet.Mode = Pet?.Mode ?? PetSheet.ModeDefence;
                Pet = sheet;
                RefreshPetItem(sheet);
                PetSummonedEvent?.Invoke(sheet);
                break;
            case PetSheet.ModeDied:
                Pet = null;
                PetGoneEvent?.Invoke();
                break;
            case PetSheet.ModeAttack:
            case PetSheet.ModeDefence:
            case PetSheet.ModeLooting:
                if (Pet is { } pet) pet.Mode = mode;
                PetModeEvent?.Invoke(mode);
                break;
        }
    }

    private void HandlePetFood(Packet p)
    {
        bool ok = p.ReadByte() == PetFoodSucceeded;
        int bagSlot = p.ReadByte();
        int itemId = p.ReadInt();
        if (!ok || p.RemainingBytes < 10)
        {
            PetFoodRefusedEvent?.Invoke(itemId);
            return;
        }
        int countLeft = p.ReadShort();
        p.ReadShort();
        p.ReadInt();
        int increase = p.ReadShort();
        int abs = InventoryConstants.InventoryStart + bagSlot;
        var left = countLeft > 0 ? new ItemSlot { ItemId = itemId, Count = (short)countLeft } : default;
        if (countLeft > 0) PreserveLastDurability(abs, ref left);
        SetLastInventorySlot(abs, left);
        InventorySlotEvent?.Invoke(abs, left);
        PetFedEvent?.Invoke(bagSlot, itemId, countLeft, increase);
    }

    private void HandlePetHatch(Packet p)
    {
        if (!PetWire.TryReadHatch(p, out var hatched, out int failure))
        {
            PetHatchFailedEvent?.Invoke(failure);
            return;
        }

        PetHatchedEvent?.Invoke(PlaceFamiliarItem(hatched), hatched.Info);
    }

    private void HandlePetTransform(Packet p)
    {
        if (!PetWire.TryReadTransform(p, out var transformed, out int failure))
        {
            PetTransformFailedEvent?.Invoke(failure);
            return;
        }

        int abs = PlaceFamiliarItem(transformed.Pet);
        if (transformed.MaterialItemId != 0)
            SpendBagItem(InventoryConstants.InventoryStart + transformed.MaterialSlot, transformed.MaterialItemId);
        PetTransformedEvent?.Invoke(abs, transformed.Pet.Info);
    }

    private int PlaceFamiliarItem(HatchedPet pet)
    {
        PetItems[pet.Info.Index] = pet.Info;
        int abs = InventoryConstants.InventoryStart + pet.BagSlot;
        var item = new ItemSlot { ItemId = pet.ItemId, Count = 1, Durability = 1, UniqueId = pet.Info.Index };
        SetLastInventorySlot(abs, item);
        InventorySlotEvent?.Invoke(abs, item);
        return abs;
    }

    private void SpendBagItem(int abs, int itemId)
    {
        var inv = LastEnter.Inventory;
        if (inv == null || abs < 0 || abs >= inv.Length || inv[abs].ItemId != itemId) return;
        var left = inv[abs];
        left.Count--;
        if (left.Count <= 0) left = default;
        SetLastInventorySlot(abs, left);
        InventorySlotEvent?.Invoke(abs, left);
    }

    private ItemSlot ReadItemRecord(Packet p)
    {
        var slot = PetWire.ReadItemRecord(p, out var pet);
        if (pet is { } info) PetItems[info.Index] = info;
        return slot;
    }

    private void RefreshPetItem(PetSheet sheet)
    {
        PetItems[sheet.Index] = new PetItemInfo(
            sheet.Index, sheet.Name, sheet.Attack, sheet.Level, sheet.ExpPercent, sheet.Satisfaction);
    }

    public void SendPetHatch(int npcId, int eggItemId, int bagSlot, string name)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(PetHatchSub);
        p.WriteInt(npcId);
        p.WriteInt(eggItemId);
        p.WriteByte((byte)bagSlot);
        p.WriteString(name);
        _conn.Send(p);
    }

    public void SendPetTransform(int npcId, int petItemId, int petSlot, int materialItemId, int materialSlot)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(PetTransformSub);
        p.WriteInt(npcId);
        p.WriteInt(petItemId);
        p.WriteByte((byte)petSlot);
        p.WriteInt(materialItemId);
        p.WriteByte((byte)materialSlot);
        for (int i = 1; i < PetTransformMaterialSlots; i++)
        {
            p.WriteInt(0);
            p.WriteByte(0);
        }
        _conn.Send(p);
    }

    public void SendPetMode(int mode)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetSubFunction);
        p.WriteByte(PetFunctionMode);
        p.WriteByte((byte)mode);
        _conn.Send(p);
    }

    public void SendPetFeed(int bagSlot, int foodItemId)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetSubFunction);
        p.WriteByte(PetFunctionFood);
        p.WriteByte((byte)bagSlot);
        p.WriteInt(foodItemId);
        p.WriteByte(PetFoodTrailFlag);
        p.WriteByte(PetFoodTrailPad);
        _conn.Send(p);
    }

    public void SendPetDismiss() => SendMagic(MagicSub.Cancel, FamiliarSummonSkill, MyCharId);

    internal void SeedPreviewPet(PetSheet sheet) => Pet = sheet;

    public void SendPetSkill(int stage, int skillId, int casterId, int targetId, int x, int y, int z)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetSubSkill);
        p.WriteByte((byte)stage);
        p.WriteInt(skillId);
        p.WriteInt(casterId);
        p.WriteInt(targetId);
        p.WriteInt(x);
        p.WriteInt(y);
        p.WriteInt(z);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteInt(0);
        _conn.Send(p);
    }
}
