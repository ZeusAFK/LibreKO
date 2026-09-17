namespace LibreKO.Network;

public struct ShoppingMallLetter
{
    public int LetterId;
    public byte Status;
    public string Subject;
    public string Sender;
    public byte Type;

    public int ItemId;
    public int Count;
    public int Coins;

    public int Date;
    public int DaysLeft;

    public readonly bool HasGift => Type == 2 && (ItemId != 0 || Coins != 0);
}
