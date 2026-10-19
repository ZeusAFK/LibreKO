namespace LibreKO.Features;

internal interface IFeature : IDisposable
{
    void Build();
}

internal interface ITickFeature
{
    void Tick(double delta, double nowSec);
}

internal interface ILateTickFeature
{
    void LateTick(double delta, double nowSec);
}

internal interface IHudTickFeature
{
    void HudTick(double delta, double nowSec);
}
