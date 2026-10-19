namespace LibreKO.Features;

internal sealed class FeatureRegistry : IDisposable
{
    private readonly List<IFeature> features = new();
    private readonly List<IFeature> built = new();
    private readonly HashSet<IFeature> builtSet = new();
    private readonly List<ITickFeature> ticking = new();
    private readonly List<ILateTickFeature> lateTicking = new();
    private readonly List<IHudTickFeature> hudTicking = new();
    private int nextToBuild;

    public bool ConstructOnly { get; set; }

    public T Add<T>(T feature) where T : IFeature
    {
        features.Add(feature);
        return feature;
    }

    public void Add(Action build, Action? dispose = null) => Add(new LambdaFeature(build, dispose));

    public void BuildPending()
    {
        if (ConstructOnly) return;
        while (nextToBuild < features.Count)
        {
            var feature = features[nextToBuild++];
            if (!builtSet.Contains(feature)) Build(feature);
        }
    }

    public T AddOnce<T>(T feature) where T : IFeature
    {
        if (!features.Contains(feature)) features.Add(feature);
        if (!builtSet.Contains(feature)) Build(feature);
        return feature;
    }

    private void Build(IFeature feature)
    {
        built.Add(feature);
        builtSet.Add(feature);
        feature.Build();
        if (feature is ITickFeature tickFeature) ticking.Add(tickFeature);
        if (feature is ILateTickFeature lateFeature) lateTicking.Add(lateFeature);
        if (feature is IHudTickFeature hudFeature) hudTicking.Add(hudFeature);
    }

    public void Tick(double delta, double nowSec)
    {
        foreach (var feature in ticking) feature.Tick(delta, nowSec);
    }

    public void LateTick(double delta, double nowSec)
    {
        foreach (var feature in lateTicking) feature.LateTick(delta, nowSec);
    }

    public void HudTick(double delta, double nowSec)
    {
        foreach (var feature in hudTicking) feature.HudTick(delta, nowSec);
    }

    public void Dispose()
    {
        for (int i = built.Count - 1; i >= 0; i--)
        {
            try { built[i].Dispose(); }
            catch (Exception e) { Diag.Report($"dispose {built[i].GetType().Name}", e); }
        }
        features.Clear();
        built.Clear();
        builtSet.Clear();
        ticking.Clear();
        lateTicking.Clear();
        hudTicking.Clear();
        nextToBuild = 0;
    }

    private sealed class LambdaFeature : IFeature
    {
        private readonly Action build;
        private readonly Action? dispose;

        public LambdaFeature(Action build, Action? dispose)
        {
            this.build = build;
            this.dispose = dispose;
        }

        public void Build() => build();

        public void Dispose() => dispose?.Invoke();
    }
}
