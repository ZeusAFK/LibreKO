namespace LibreKO.Session;

public sealed class PieceChangeSession
{
    public event Action<int>? PieceChangeOpenEvent;

    internal void RaisePieceChangeOpen(int value) => PieceChangeOpenEvent?.Invoke(value);

    public event Action<PieceExchangeResult>? PieceExchangeResultEvent;

    internal void RaisePieceExchangeResult(PieceExchangeResult pieceExchangeResult) => PieceExchangeResultEvent?.Invoke(pieceExchangeResult);
}
