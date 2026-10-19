namespace LibreKO.Network.Readers;

internal sealed class MessengerReader : IPacketReader
{
    private readonly MessengerSession messenger;

    public MessengerReader(MessengerSession messenger)
    {
        this.messenger = messenger;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MESSENGER, HandleMessenger);
    }

    private void HandleMessenger(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (MessengerSubOpcode)p.ReadByte();
        if (sub == MessengerSubOpcode.List)
        {
            var list = new List<MessengerBuddy>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
            {
                int charId = p.ReadInt();
                string name = p.ReadSByteString().Trim();
                byte online = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                list.Add(new MessengerBuddy { CharId = charId, Name = name, Online = online != 0 });
            }
            messenger.RaiseMessengerList(list);
        }
        else if (sub == MessengerSubOpcode.Send)
        {
            byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
            string toName = p.RemainingBytes > 0 ? p.ReadSByteString().Trim() : "";
            messenger.RaiseMessengerSendResult(toName, result);
        }
    }
}
