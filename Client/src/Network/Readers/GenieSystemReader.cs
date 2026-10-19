namespace LibreKO.Network.Readers;

internal sealed class GenieSystemReader : IPacketReader
{
    private readonly GenieSession genie;

    public GenieSystemReader(GenieSession genie)
    {
        this.genie = genie;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_GENIE_SYSTEM, HandleGenieSystem);
    }

    private void HandleGenieSystem(Packet p)
    {
        if (p.RemainingBytes < 2 || (GenieSystemSubOpcode)p.ReadByte() != GenieSystemSubOpcode.InfoRequest) return;
        var command = (GenieSystemCommand)p.ReadByte();
        if (command == GenieSystemCommand.UseHammer)
        {
            if (p.RemainingBytes >= 1) genie.RaiseGenieHammerResult((GenieState)p.ReadByte() == GenieState.Active);
            return;
        }
        if (command == GenieSystemCommand.Activated)
        {
            if (p.RemainingBytes >= 5) genie.RaiseGenieAura(p.ReadInt(), (GenieState)p.ReadByte() == GenieState.Active);
            return;
        }
        if (command == GenieSystemCommand.LoadOptions)
        {
            if (p.RemainingBytes < GenieSystemWire.OptionBytes) return;
            var options = new byte[GenieSystemWire.OptionBytes];
            for (int i = 0; i < options.Length; i++) options[i] = p.ReadByte();
            genie.RaiseGenieOptionsReceived(options);
            return;
        }
        if (command is GenieSystemCommand.Start or GenieSystemCommand.Stop)
        {
            if (p.RemainingBytes < 4 || (GenieSystemResult)p.ReadUShort() != GenieSystemResult.Acknowledged) return;
            int minutes = p.ReadUShort();
            genie.ApplySystemState(command == GenieSystemCommand.Start && minutes > 0, minutes);
        }
        else if ((command is GenieSystemCommand.UseSpiritPotion or GenieSystemCommand.RemainingTime) && p.RemainingBytes >= 2)
        {
            genie.ApplyRemainingTime(p.ReadUShort());
        }
    }
}
