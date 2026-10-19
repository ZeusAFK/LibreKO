using Godot;

namespace LibreKO.Features.Hud;

internal sealed class RenderFaultFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;

    public RenderFaultFeature(IWorldContext context)
    {
        this.context = context;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    public void Tick(double delta, double nowSec)
    {
        RenderFaultTick();
    }

    private const double ReportSeconds = 10;

    private int faultsSeen;
    private double reportAt;

    private void RenderFaultTick()
    {
        int faults = RenderFaultWatch.Faults;
        if (faults == faultsSeen) return;
        int fresh = faults - faultsSeen;
        if (Config.FxLayer)
        {
            faultsSeen = faults;
            GD.Print($"[render] missing uniform set (1) x{fresh}: zone {context.Zone}, fx users {FxLayer.Users}, moving {context.Self.Moving}; accurate effect blending off for this session");
            Config.SuspendFxLayer();
            context.Log.Notice("Effects switched to simple blending for this session after a rendering error.");
            return;
        }
        double now = context.Now;
        if (now < reportAt) return;
        reportAt = now + ReportSeconds;
        faultsSeen = faults;
        GD.Print($"[render] missing uniform set (1) x{fresh} with accurate effect blending off: zone {context.Zone}, moving {context.Self.Moving}");
    }
}
