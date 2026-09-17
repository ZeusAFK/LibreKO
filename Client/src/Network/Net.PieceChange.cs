using System;

namespace LibreKO.Network;

public readonly record struct PieceExchangeResult(
    byte ResultCode,
    int RewardItemId,
    int RewardPosition,
    int PieceItemId,
    int PiecePosition,
    byte Effect);

public partial class Net
{
    public event Action<int>? PieceChangeOpenEvent;
    public event Action<PieceExchangeResult>? PieceExchangeResultEvent;

    public const byte PieceEffectRed = 1;
    public const byte PieceEffectGreen = 2;
    public const byte PieceEffectWhite = 3;

    public const byte PieceResultFailed = 0;
    public const byte PieceResultSucceeded = 1;
    public const byte PieceResultRejected = 2;
    public const byte PieceResultNoPiece = 4;

    private const byte PieceChangeOpenSub = 4;
    private const byte PieceChangeExchangeSub = 5;

    private void HandlePieceChangeOpen(Packet p)
    {
        if (p.RemainingBytes < 4) return;
        PieceChangeOpenEvent?.Invoke(p.ReadInt());
    }

    private void HandlePieceExchangeResult(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte result = p.ReadByte();

        int rewardItemId = 0, rewardPosition = -1, pieceItemId = 0, piecePosition = -1;
        byte effect = 0;

        if (result == PieceResultSucceeded && p.RemainingBytes >= 5)
        {
            rewardItemId = p.ReadInt();
            rewardPosition = unchecked((sbyte)p.ReadByte());
            if (p.RemainingBytes >= 5)
            {
                pieceItemId = p.ReadInt();
                piecePosition = unchecked((sbyte)p.ReadByte());
            }
            if (p.RemainingBytes >= 1)
                effect = p.ReadByte();
        }

        PieceExchangeResultEvent?.Invoke(new PieceExchangeResult(
            result, rewardItemId, rewardPosition, pieceItemId, piecePosition, effect));
    }

    public void SendPieceExchange(int npcId, int pieceItemId, int piecePosition)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(PieceChangeExchangeSub);
        p.WriteInt(npcId);
        p.WriteInt(pieceItemId);
        p.WriteByte(piecePosition >= 0 ? (byte)piecePosition : byte.MaxValue);
        _conn.Send(p);
    }
}
