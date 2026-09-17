using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int MiningError = 0, MiningSuccess = 1, MiningAlready = 2, MiningNotArea = 3,
                     MiningPreparing = 4, MiningNoTool = 5, MiningNothing = 6, MiningNoBait = 7;
    public const int FxMiningItem = 13081, FxGatherXp = 13082, FxFishingItem = 30730;

    public const byte SubMiningStart = 1, SubMiningAttempt = 2, SubMiningStop = 3;
    public const byte SubFishingStart = 6, SubFishingAttempt = 7, SubFishingStop = 8;

    private const int StopBroadcast = 1, StopSelf = 2;

    public event Action<int, int, int>? GatherStartEvent;
    public event Action<int, int, int, int>? GatherResultEvent;
    public event Action<int, int, bool>? GatherStopEvent;
    public event Action<int, short>? ItemDurationEvent;

    private void HandleMining(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        int sub = p.ReadByte();
        int code = p.ReadUShort();

        switch (sub)
        {
            case SubMiningStart:
            case SubFishingStart:
            {
                int charId = code == MiningSuccess && p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                GatherStartEvent?.Invoke(sub, code, charId);
                break;
            }

            case SubMiningAttempt:
            case SubFishingAttempt:
            {
                int charId = -1, effect = 0;
                if (code == MiningSuccess && p.RemainingBytes >= 6)
                {
                    charId = p.ReadInt();
                    effect = p.ReadUShort();
                }
                GatherResultEvent?.Invoke(sub, code, charId, effect);
                break;
            }

            case SubMiningStop:
            case SubFishingStop:
            {
                if (code == StopBroadcast && p.RemainingBytes >= 4)
                    GatherStopEvent?.Invoke(sub, p.ReadInt(), false);
                else if (code == StopSelf)
                    GatherStopEvent?.Invoke(sub, -1, true);
                break;
            }
        }
    }

    private void HandleDuration(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        int position = p.ReadByte();
        short dura = p.ReadShort();
        ItemDurationEvent?.Invoke(position, dura);
    }

    public void SendMiningStart()   => SendMiningSub(SubMiningStart);
    public void SendMiningAttempt() => SendMiningSub(SubMiningAttempt);
    public void SendMiningStop()    => SendMiningSub(SubMiningStop);
    public void SendFishingStart()  => SendMiningSub(SubFishingStart);
    public void SendFishingAttempt()=> SendMiningSub(SubFishingAttempt);
    public void SendFishingStop()   => SendMiningSub(SubFishingStop);

    private void SendMiningSub(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_MINING);
        p.WriteByte(sub);
        _conn.Send(p);
    }
}
