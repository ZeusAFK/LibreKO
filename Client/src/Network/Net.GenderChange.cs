using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte GenderChangeRequest = 1;
    public const byte GenderChangeFailed = 0;
    public const byte GenderChangeDone = 1;
    public const byte GenderChangeNoItem = 2;

    public event Action<int>? GenderChangeRefusedEvent;
    public event Action? GenderChangedEvent;

    private void HandleGenderChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte result = p.ReadByte();
        if (result != GenderChangeDone)
        {
            GenderChangeRefusedEvent?.Invoke(result);
            return;
        }
        if (p.RemainingBytes < 10) return;
        int id = p.ReadInt();
        byte race = p.ReadByte();
        byte face = p.ReadByte();
        int hair = p.ReadInt();
        if (id != LastEnter.CharId) return;
        var info = LastEnter;
        info.Race = race;
        info.Face = face;
        info.Hair = hair;
        LastEnter = info;
        GenderChangedEvent?.Invoke();
    }

    public void SendGenderChange(int race, int face, int hair)
    {
        var p = new Packet(GameOpcodes.GS_GENDER_CHANGE);
        p.WriteByte(GenderChangeRequest);
        p.WriteByte((byte)race);
        p.WriteByte((byte)face);
        p.WriteInt(hair);
        _conn.Send(p);
    }
}
