using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte PetSubModeFunction = 1;

    private const byte PetCodeNormal       = 5;
    private const byte PetCodeSatisfaction = 0x0F;
    private const byte PetCodeFood         = 0x10;

    public const byte PetModeAttack  = 3;
    public const byte PetModeDefence = 4;
    public const byte PetModeLooting = 8;
    public const byte PetModeChat    = 9;
    public const byte PetCodeDeath   = 2;

    public const short PetMaxSatisfaction = 10000;

    public const int PetFood20  = 389570000;
    public const int PetFood50  = 389580000;
    public const int PetFood100 = 389590000;

    public event Action<byte>? PetModeEvent;

    public event Action<int, int>? PetSatisfactionEvent;

    public event Action<int, int>? PetFedEvent;

    public event Action<int>? PetDeathEvent;

    private void HandlePet(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte sub = p.ReadByte();
        if (sub != PetSubModeFunction) return;

        byte code = p.ReadByte();
        switch (code)
        {
            case PetCodeNormal:
            {
                if (p.RemainingBytes < 1) return;
                byte mode = p.ReadByte();
                if (mode == PetCodeDeath)
                {
                    p.ReadUShort();
                    int nid = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    PetDeathEvent?.Invoke(nid);
                }
                else if (mode == PetModeAttack || mode == PetModeDefence || mode == PetModeLooting)
                {
                    PetModeEvent?.Invoke(mode);
                }
                break;
            }
            case PetCodeSatisfaction:
            {
                if (p.RemainingBytes < 6) return;
                int sat = p.ReadUShort();
                int nid = p.ReadInt();
                PetSatisfactionEvent?.Invoke(sat, nid);
                break;
            }
            case PetCodeFood:
            {
                if (p.RemainingBytes < 6) return;
                int oldSat = p.ReadUShort();
                int itemId = p.ReadInt();
                PetFedEvent?.Invoke(itemId, oldSat);
                break;
            }
        }
    }

    public void SendPetMode(byte mode)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetSubModeFunction);
        p.WriteByte(PetCodeNormal);
        p.WriteByte(mode);
        _conn.Send(p);
    }

    public void SendPetFeed(byte slotIndex, int foodItemId)
    {
        var p = new Packet(GameOpcodes.GS_PET);
        p.WriteByte(PetSubModeFunction);
        p.WriteByte(PetCodeFood);
        p.WriteByte(slotIndex);
        p.WriteInt(foodItemId);
        _conn.Send(p);
    }
}
