using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte SiegeBaseCreate = 1, SiegeCastleFlag = 2, SiegeMoradonNpc = 3, SiegeDelosNpc = 4,
        SiegeRank = 5;
    private const byte SiegeMoradonSchedule = 2, SiegeMoradonMaster = 4, SiegeMoradonStatus = 5;

    public event Action<SiegeCastleOwner>? SiegeCastleFlagEvent;
    public event Action<SiegeSchedule>? SiegeScheduleEvent;
    public event Action<SiegeMasterInfo>? SiegeMasterEvent;
    public event Action<SiegeStatus>? SiegeStatusEvent;

    private void HandleSiege(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte opcode = p.ReadByte();
        switch (opcode)
        {
            case SiegeCastleFlag: HandleSiegeCastleFlag(p); break;
            case SiegeMoradonNpc: HandleSiegeMoradon(p); break;
        }
    }

    private void HandleSiegeCastleFlag(Packet p)
    {
        if (p.RemainingBytes >= 1) p.ReadByte();
        var owner = new SiegeCastleOwner();
        if (p.RemainingBytes >= 6)
        {
            owner.ClanId = p.ReadUShort();
            owner.Mark = p.ReadUShort();
            owner.Flag = (byte)p.ReadByte();
            owner.Grade = (byte)p.ReadByte();
        }
        SiegeCastleFlagEvent?.Invoke(owner);
    }

    private void HandleSiegeMoradon(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case SiegeMoradonSchedule:
            {
                var s = new SiegeSchedule();
                s.CastleIndex = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                s.SiegeType = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                s.WarDay = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                s.WarHour = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                s.WarMinute = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                SiegeScheduleEvent?.Invoke(s);
                break;
            }
            case SiegeMoradonMaster:
            {
                var m = new SiegeMasterInfo();
                m.CastleIndex = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                if (p.RemainingBytes >= 1) p.ReadByte();
                m.ClanName = p.ReadString();
                m.Nation = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                m.Members = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                m.RequestDay = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                m.RequestHour = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                m.RequestMinute = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                SiegeMasterEvent?.Invoke(m);
                break;
            }
            case SiegeMoradonStatus:
            {
                var st = new SiegeStatus();
                st.CastleIndex = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                st.SiegeType = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                st.ClanName = p.ReadString();
                st.Nation = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                st.Members = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                SiegeStatusEvent?.Invoke(st);
                break;
            }
        }
    }

    public void SendSiegeCastleFlag() => SendSiegeByte(SiegeCastleFlag);

    public void SendSiegeSchedule() => SendSiegeOp(SiegeMoradonNpc, SiegeMoradonSchedule);

    public void SendSiegeMaster() => SendSiegeOp(SiegeMoradonNpc, SiegeMoradonMaster);

    public void SendSiegeStatus() => SendSiegeOp(SiegeMoradonNpc, SiegeMoradonStatus);

    private void SendSiegeByte(byte opcode)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        p.WriteByte(opcode);
        _conn.Send(p);
    }

    private void SendSiegeOp(byte opcode, byte sub)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        p.WriteByte(opcode);
        p.WriteByte(sub);
        _conn.Send(p);
    }
}
