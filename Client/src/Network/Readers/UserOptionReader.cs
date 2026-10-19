namespace LibreKO.Network.Readers;

internal sealed class UserOptionReader : IPacketReader
{
    private readonly UserOptionSession userOption;

    private const int RankAuraRecordBytes = 4 + 1;

    public UserOptionReader(UserOptionSession userOption)
    {
        this.userOption = userOption;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_USER_OPTION, HandleUserOption);
    }

    private void HandleUserOption(Packet p)
    {
        if (p.RemainingBytes < 1 + RankAuraRecordBytes || (UserOptionSubOpcode)p.ReadByte() != UserOptionSubOpcode.RankAura) return;
        int id = p.ReadInt();
        bool shown = p.ReadByte() != 0;
        userOption.SetRankAuraShown(id, shown);
        userOption.RaiseRankAuraShown(id, shown);
    }
}
