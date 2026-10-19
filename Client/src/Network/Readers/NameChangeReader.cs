namespace LibreKO.Network.Readers;

internal sealed class NameChangeReader : IPacketReader
{
    private readonly ClanSession clan;
    private readonly NameChangeSession nameChange;

    public NameChangeReader(ClanSession clan, NameChangeSession nameChange)
    {
        this.clan = clan;
        this.nameChange = nameChange;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_NAME_CHANGE, HandleNameChange);
    }

    private void HandleNameChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte code = p.ReadByte();

        if ((NameChangeSubOpcode)code == NameChangeSubOpcode.Clan)
        {
            HandleClanNameChange(p);
            return;
        }

        if ((NameChangeResult)code == NameChangeResult.Success)
            nameChange.RaiseNameChangeSuccess("");
        else
            nameChange.RaiseNameChangeResult((NameChangeResult)code);
    }

    private void HandleClanNameChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var code = (ClanNameChangeResult)p.ReadByte();

        if (code != ClanNameChangeResult.Success)
        {
            nameChange.RaiseClanNameChangeResult(code);
            return;
        }

        string name = p.RemainingBytes >= 2 ? p.ReadString() : "";
        if (name.Length > 0 && clan.MyClan.InClan)
        {
            var renamed = clan.MyClan;
            renamed.Name = name;
            clan.ApplyMyClan(renamed);
        }
        nameChange.RaiseClanRenamed(name);
    }
}
