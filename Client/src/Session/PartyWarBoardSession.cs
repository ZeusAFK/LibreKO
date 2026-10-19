namespace LibreKO.Session;

public sealed class PartyWarBoardSession
{
    public event Action<WarBoardReply>? PartyWarBoardEvent;

    internal void RaisePartyWarBoard(WarBoardReply warBoardReply) => PartyWarBoardEvent?.Invoke(warBoardReply);

    public PartyBoardMeterMode PartyBoardMeterMode { get; private set; } = PartyBoardMeterMode.Off;

    internal void ApplyMeterMode(PartyBoardMeterMode mode) => PartyBoardMeterMode = mode;
}
