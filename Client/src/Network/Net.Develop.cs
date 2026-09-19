using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, int>? SkillPointChangeEvent;

    public event Action<bool, int, int>? SkillResetEvent;

    public event Action<bool, int, int[], int, int, int, int>? StatResetEvent;

    public event Action? ClassChangeNpcEvent;

    public event Action<int>? ClassEligibilityEvent;

    public event Action<int>? JobChangeResultEvent;

    public event Action<int, int>? ClassPromotedEvent;

    public event Action<int, int>? RebStatChangeEvent;

    public event Action<int>? ResetCostEvent;

    private void HandleSkillPointChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int type = p.ReadByte();
        int value = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        SkillPointChangeEvent?.Invoke(type, value);
    }

    private void HandleClassChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1: ClassChangeNpcEvent?.Invoke(); break;
            case 2: ParseStatReset(p); break;
            case 3: ParseSkillReset(p); break;
            case 4: ResetCostEvent?.Invoke(p.RemainingBytes >= 4 ? p.ReadInt() : 0); break;
            case 5: ClassEligibilityEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadByte() : 0); break;
            // Two shapes share sub 6: the job-change panel answers [byte result], the quest promotion
            // (ScriptCharacterService.ApplyPromotion) sends [short newClass][int charId].
            case 6:
                if (p.RemainingBytes >= 6)
                {
                    int newClass = p.ReadShort();
                    int charId = p.ReadInt();
                    if (charId == MyCharId) ApplyOwnClass(newClass);
                    ClassPromotedEvent?.Invoke(charId, newClass);
                }
                else JobChangeResultEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case 7:
            case 8: RebStatChangeEvent?.Invoke(sub, p.RemainingBytes >= 1 ? p.ReadByte() : 0); break;
        }
    }

    private void ParseSkillReset(Packet p)
    {
        byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
        if (result == 1)
        {
            int money = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
            int pool = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            if (money >= 0) GoldChangeEvent?.Invoke(money);
            SkillResetEvent?.Invoke(true, money, pool);
        }
        else
        {
            int cost = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            SkillResetEvent?.Invoke(false, cost, 0);
        }
    }

    private void ParseStatReset(Packet p)
    {
        byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
        if (result == 1 && p.RemainingBytes >= 4)
        {
            int money = p.ReadInt();
            var stats = new int[5];
            for (int i = 0; i < 5; i++) stats[i] = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int maxHp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int maxMp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            int ap = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            if (p.RemainingBytes >= 4) p.ReadInt();
            int statPoints = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            GoldChangeEvent?.Invoke(money);
            StatResetEvent?.Invoke(true, money, stats, maxHp, maxMp, ap, statPoints);
        }
        else
        {
            int cost = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            StatResetEvent?.Invoke(false, cost, System.Array.Empty<int>(), 0, 0, 0, 0);
        }
    }

    public void SendSkillPointChange(int treeType) => SendClassByte(GameOpcodes.GS_SKILLPT_CHANGE, (byte)treeType);

    public void SendStatReset() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, 2);

    public void SendSkillReset() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, 3);

    public const byte ResetKindStat = 1;
    public const byte ResetKindSkill = 2;

    public void SendResetCostQuery(byte kind)
    {
        var p = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        p.WriteByte(4);
        p.WriteByte(kind);
        _conn.Send(p);
    }

    public void SendClassEligibilityQuery() => SendClassByte(GameOpcodes.GS_CLASS_CHANGE, 1);

    private void SendClassByte(GameOpcodes opcode, byte sub)
    {
        var p = new Packet(opcode);
        p.WriteByte(sub);
        _conn.Send(p);
    }
}
