namespace LibreKO.Plugins;

public readonly record struct GameCape(int Id, int Cloth, int Mark, string Name, bool Locked, bool Castle);

public interface IGameCapeShop
{
    bool IsOpen { get; }
    IReadOnlyList<GameCape> Capes { get; }
    int Choice { get; }
    int Current { get; }
    string MoneyLine { get; }
    string ContLine { get; }
    string GradeLine { get; }
    bool GradeShort { get; }
    string Notice { get; }
    string Status { get; }
    bool CanBuy { get; }
    bool CanVoucher { get; }

    void Select(int capeId);
    void Buy();
    void Voucher();
    void Close();

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameCapeShop CapeShop => capeShop;

    private readonly CapeShopProxy capeShop = new();

    internal void AttachCapeShop(IGameCapeShop capeShopSource)
    {
        capeShop.Source = capeShopSource;
        RaiseCapeShop();
    }

    internal void RaiseCapeShop() => capeShop.Raise();

    private sealed class CapeShopProxy : IGameCapeShop
    {
        public IGameCapeShop? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public bool IsOpen => Source?.IsOpen ?? false;
        public IReadOnlyList<GameCape> Capes => Source?.Capes ?? Array.Empty<GameCape>();
        public int Choice => Source?.Choice ?? -1;
        public int Current => Source?.Current ?? -1;
        public string MoneyLine => Source?.MoneyLine ?? "";
        public string ContLine => Source?.ContLine ?? "";
        public string GradeLine => Source?.GradeLine ?? "";
        public bool GradeShort => Source?.GradeShort ?? false;
        public string Notice => Source?.Notice ?? "";
        public string Status => Source?.Status ?? "";
        public bool CanBuy => Source?.CanBuy ?? false;
        public bool CanVoucher => Source?.CanVoucher ?? false;
        public void Select(int capeId) => Source?.Select(capeId);
        public void Buy() => Source?.Buy();
        public void Voucher() => Source?.Voucher();
        public void Close() => Source?.Close();
    }
}
