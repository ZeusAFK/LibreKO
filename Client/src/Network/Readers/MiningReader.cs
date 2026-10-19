namespace LibreKO.Network.Readers;

internal sealed class MiningReader : IPacketReader
{
    private readonly MiningSession mining;

    private const int YutRollPaddingBytes = 2;
    private const int YutRollTailBytes = 4 + YutRollPaddingBytes + 1 + 1;

    public MiningReader(MiningSession mining)
    {
        this.mining = mining;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<MiningSubOpcode>(GameOpcodes.GS_MINING, HandleMining);
        dispatcher.On(GameOpcodes.GS_DURATION, HandleDuration);
    }

    private void HandleMining(Packet p, MiningSubOpcode sub)
    {
        if (p.RemainingBytes < 2) return;
        if (sub == MiningSubOpcode.BettingGame)
        {
            HandleYutRoll(p);
            return;
        }
        var code = (MiningResult)p.ReadUShort();

        switch (sub)
        {
            case MiningSubOpcode.MiningStart:
            case MiningSubOpcode.FishingStart:
                {
                    int charId = code == MiningResult.Success && p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                    mining.RaiseGatherStart(sub, code, charId);
                    break;
                }

            case MiningSubOpcode.MiningAttempt:
            case MiningSubOpcode.FishingAttempt:
                {
                    int charId = -1, effect = 0;
                    if (code == MiningResult.Success && p.RemainingBytes >= 6)
                    {
                        charId = p.ReadInt();
                        effect = p.ReadUShort();
                    }
                    mining.RaiseGatherResult(sub, code, charId, effect);
                    break;
                }

            case MiningSubOpcode.MiningStop:
            case MiningSubOpcode.FishingStop:
                {
                    if (code == MiningResult.Success && p.RemainingBytes >= 4)
                        mining.RaiseGatherStop(sub, p.ReadInt(), false);
                    else if (code == MiningResult.Already)
                        mining.RaiseGatherStop(sub, -1, true);
                    break;
                }
        }
    }

    private void HandleYutRoll(Packet p)
    {
        var result = (YutResult)p.ReadUShort();
        if (p.RemainingBytes < YutRollTailBytes) return;
        int charId = p.ReadInt();
        p.Skip(YutRollPaddingBytes);
        int playerRoll = p.ReadByte();
        int npcRoll = p.ReadByte();
        mining.RaiseYutRoll(new YutRoll(result, charId, playerRoll, npcRoll));
    }

    private void HandleDuration(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        int position = p.ReadByte();
        short dura = p.ReadShort();
        mining.RaiseItemDuration(position, dura);
    }
}
