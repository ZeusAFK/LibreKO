namespace LibreKO.Network.Readers;

internal sealed class GenderChangeReader : IPacketReader
{
    private readonly GenderChangeSession genderChange;
    private readonly SelfSession self;

    public GenderChangeReader(GenderChangeSession genderChange, SelfSession self)
    {
        this.genderChange = genderChange;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_GENDER_CHANGE, HandleGenderChange);
    }

    private void HandleGenderChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var result = (GenderChangeResult)p.ReadByte();
        if (result != GenderChangeResult.Changed)
        {
            genderChange.RaiseGenderChangeRefused(result);
            return;
        }
        if (p.RemainingBytes < 10) return;
        int id = p.ReadInt();
        byte race = p.ReadByte();
        byte face = p.ReadByte();
        int hair = p.ReadInt();
        if (id != self.LastEnter.CharId) return;
        self.ChangeBody(race, face, hair);
        genderChange.RaiseGenderChanged();
    }
}
