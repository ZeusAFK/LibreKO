namespace LibreKO.Network.Readers;

internal sealed class BifrostReader : IPacketReader
{
    private readonly BifrostSession bifrost;
    private readonly Func<ulong> nowMsec;

    private const int DrakiTimerHeaderBytes = 2;

    public BifrostReader(BifrostSession bifrost, Func<ulong> nowMsec)
    {
        this.bifrost = bifrost;
        this.nowMsec = nowMsec;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_BIFROST, TempleSubOpcode.BifrostRemaining, HandleBifrostRemaining);
        dispatcher.OnOtherSubs<TempleSubOpcode>(GameOpcodes.GS_EVENT, HandleBifrostEvent);
    }

    private void HandleBifrostRemaining(Packet p)
    {
        int remaining = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (remaining < 0) remaining = 0;
        var eventType = p.RemainingBytes >= 1 ? (TempleEventType)p.ReadByte() : TempleEventType.None;
        bifrost.RaiseBifrostTime(remaining, eventType);
    }

    private void HandleBifrostEvent(Packet p, TempleSubOpcode sub)
    {
        switch (sub)
        {
            case TempleSubOpcode.FullMoonEntry:
                if (p.RemainingBytes >= 1) bifrost.RaiseFullMoonEntry(p.ReadByte());
                break;
            case TempleSubOpcode.FullMoonTower:
                if (p.RemainingBytes >= 1) bifrost.RaiseFullMoonTower(p.ReadByte());
                break;
            case TempleSubOpcode.FullMoonStage:
                if (p.RemainingBytes >= 2) bifrost.RaiseFullMoonStage(p.ReadByte(), p.ReadByte());
                break;
            case TempleSubOpcode.TempleEventJoin:
                {
                    var result = p.RemainingBytes >= 1 ? (TempleJoinResult)p.ReadByte() : TempleJoinResult.NotOpen;
                    int zone = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    bifrost.RaiseBifrostJoin(result, zone);
                    break;
                }
            case TempleSubOpcode.TempleEventCounter:
                {
                    if (p.RemainingBytes < 6) break;
                    int eventId = p.ReadUShort();
                    int karus = p.ReadUShort();
                    int elMorad = p.ReadUShort();
                    bifrost.RaiseTempleSignUps(eventId, karus, elMorad);
                    break;
                }
            case TempleSubOpcode.TempleEventDisband:
                {
                    if (p.RemainingBytes >= 1) p.ReadByte();
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    bifrost.RaiseBifrostDisband();
                    break;
                }
            case TempleSubOpcode.TempleScreen:
                {
                    int karus = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    int elmo = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    bifrost.BorderWar.SetScores(karus, elmo);
                    bifrost.RaiseTempleScreenScore(karus, elmo);
                    break;
                }
            case TempleSubOpcode.AltarKilledMessage:
                {
                    string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : string.Empty;
                    byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    bifrost.BorderWar.FragmentTaken(name, nation);
                    bifrost.RaiseAltarFlag(name, nation);
                    break;
                }
            case TempleSubOpcode.AltarTimer:
                {
                    int secs = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    bifrost.BorderWar.AltarTimer(secs, DateTime.UtcNow);
                    bifrost.RaiseAltarTimer(secs);
                    break;
                }
            case TempleSubOpcode.DrakiTimer:
                {
                    if (p.RemainingBytes < DrakiTimerHeaderBytes + 2 + 2 + 4 + 4) break;
                    p.ReadUShort();
                    int stage = p.ReadUShort();
                    int subStage = p.ReadUShort();
                    int limit = p.ReadInt();
                    int elapsed = p.ReadInt();
                    bifrost.ApplyDrakiTimer(stage, subStage, Math.Max(0, limit - elapsed), nowMsec());
                    break;
                }
            case TempleSubOpcode.DrakiLeaveFirst:
                bifrost.ApplyDrakiLeave();
                break;
            case TempleSubOpcode.DrakiLeaveSecond:
                if (DrakiWire.ReadStageEnd(p) is { } finish) bifrost.ApplyDrakiStageEnd(finish);
                break;
            case TempleSubOpcode.DrakiEnter:
                if (p.RemainingBytes >= 1) bifrost.RaiseDrakiEnterResult((DrakiEnterResult)p.ReadByte());
                break;
            case TempleSubOpcode.DrakiList:
                bifrost.RaiseDrakiRankList(DrakiWire.ReadRankList(p));
                break;
        }
    }
}
