namespace LibreKO.Features.Windows;

internal sealed class SeasonalFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SeasonalSession seasonal;

    public SeasonalFeature(IWorldContext context, SeasonalSession seasonal)
    {
        this.context = context;
        this.seasonal = seasonal;
    }

    public void Build() => seasonal.SantaEvent += OnSanta;

    public void Dispose() => seasonal.SantaEvent -= OnSanta;

    private void OnSanta(int state)
    {
        if (Seasonal.FxFor(state) is { } fxId && Fx.NameForId(fxId) is { } fx)
            context.Entities.SpawnFxOn(context.Self.CharId, fx, 0f);
    }
}
