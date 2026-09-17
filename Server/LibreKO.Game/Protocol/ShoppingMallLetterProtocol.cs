using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol;

internal static class ShoppingMallLetterProtocol
{
    public const byte StoreLetter = 6;

    public const byte LetterUnread = 1;
    public const byte LetterList = 2;
    public const byte LetterHistory = 3;
    public const byte LetterGetItem = 4;
    public const byte LetterRead = 5;
    public const byte LetterSend = 6;
    public const byte LetterDelete = 7;

    public const int LetterSendCost = 1000;
    public const int LetterSendItemCost = 10000;
    public const int MaxLetterSubject = 31;
    public const int MaxLetterMessage = 128;
    public const byte MaxDeleteCount = 5;

    public static Packet CreateLetterPacket(byte subOpcode) =>
        Writers.ShoppingMallPacketWriter.Sub(StoreLetter, subOpcode);
}
