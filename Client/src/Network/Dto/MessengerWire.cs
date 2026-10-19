namespace LibreKO.Network;

public enum MessengerSubOpcode : byte
{
    List = 1,
    Send = 2,
}

public struct MessengerBuddy
{
    public int CharId;
    public string Name;
    public bool Online;
}
