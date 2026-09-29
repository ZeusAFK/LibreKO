namespace LibreKO;

public readonly record struct FxPartKey(string Name, int Index, float Scale, bool Additive);

public interface IFxPooledPart
{
    FxPartKey? PoolKey { get; set; }
    void Reset();
}
