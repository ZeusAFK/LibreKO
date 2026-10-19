using Godot;

namespace LibreKO.Features.Core;

internal sealed class StealthSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly StealthSession stealth;
    private readonly IStealthRequests stealthRequests;

    public StealthSystem(IWorldContext context, StealthSession stealth, IStealthRequests stealthRequests)
    {
        this.context = context;
        this.stealth = stealth;
        this.stealthRequests = stealthRequests;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal HashSet<int> StealthIds { get; } = new();
    private readonly HashSet<int> stealthDetected = new();
    private readonly HashSet<int> infiltratingIds = new();
    private float sightRadius;

    private const float StealthSeenAlpha = 0.5f;

    private const string StealthFx = "stealth_cast";
    private const double StealthFxSeconds = 1.2;

    internal void StealthInit()
    {
        stealth.StealthEvent += OnStealth;
        stealth.SightEvent += OnSight;
    }

    internal void StealthDispose()
    {
        stealth.SightEvent -= OnSight;
        stealth.StealthEvent -= OnStealth;
    }

    private void OnSight(float radius)
    {
        if (radius <= 0f && sightRadius <= 0f) return;

        sightRadius = radius;
        if (radius <= 0f) stealthDetected.Clear();
        context.Log.Notice(radius > 0f ? "You can see the hidden." : "The hidden fade from view.");
        foreach (int id in StealthIds) ApplyStealthFade(id, stealthed: true);
    }

    internal void StealthTick(Vector3 selfPos)
    {
        if (StealthIds.Count == 0) return;

        float radiusSq = sightRadius * sightRadius;
        foreach (int id in StealthIds)
        {
            if (id == context.Self.CharId || infiltratingIds.Contains(id)) continue;

            var body = StealthBodyOf(id);
            bool seen = sightRadius > 0f && body != null
                && body.Position.DistanceSquaredTo(selfPos) <= radiusSq;
            if (seen == stealthDetected.Contains(id)) continue;

            if (seen) stealthDetected.Add(id);
            else stealthDetected.Remove(id);
            ApplyStealthFade(id, stealthed: true);
        }
    }

    internal void StealthCancelSelf()
    {
        if (!StealthIds.Contains(context.Self.CharId)) return;
        stealthRequests.Send();
        context.Log.Notice("Cancelling stealth.");
    }

    internal void StealthOnSpawn(int charId, InvisibilityType invisibility)
    {
        SetInfiltrating(charId, invisibility == InvisibilityType.Infiltration);
        if (!StealthIds.Add(charId)) return;
        ApplyStealthFade(charId, stealthed: true);
    }

    private void SetInfiltrating(int charId, bool on)
    {
        if (on) infiltratingIds.Add(charId); else infiltratingIds.Remove(charId);
        if (charId != context.Self.CharId && context.Entities.Views.TryGetValue(charId, out var ent)) ent.Infiltrating = on;
    }

    internal void StealthForgetEntity(int charId)
    {
        StealthIds.Remove(charId);
        stealthDetected.Remove(charId);
        infiltratingIds.Remove(charId);
    }

    private void OnStealth(int charId, InvisibilityType value)
    {
        bool on = value != InvisibilityType.None;
        if (on)
        {
            SetInfiltrating(charId, value == InvisibilityType.Infiltration);
            if (!StealthIds.Add(charId))
            {
                ApplyStealthFade(charId, stealthed: true);
                return;
            }
            ApplyStealthFade(charId, stealthed: true);
            StealthSpawnFx(charId);

            if (charId == context.Self.CharId)
                context.Log.Notice("You melt into the shadows.");
            else if (context.Entities.Views.TryGetValue(charId, out var e) && !string.IsNullOrEmpty(e.Name))
                context.Log.Notice($"{e.Name} vanishes.");
        }
        else
        {
            SetInfiltrating(charId, false);
            if (!StealthIds.Remove(charId)) return;
            stealthDetected.Remove(charId);
            ApplyStealthFade(charId, stealthed: false);

            if (charId == context.Self.CharId)
                context.Log.Notice("You step back into the light.");
        }
    }

    internal event Action<int, bool>? StealthLookChanged;

    internal void ApplyStealthFade(int charId, bool stealthed)
    {
        var body = StealthBodyOf(charId);
        if (body == null) return;

        float alpha;
        if (!stealthed) alpha = 1f;
        else if (charId == context.Self.CharId) alpha = StealthSeenAlpha;
        else if (infiltratingIds.Contains(charId)) alpha = StealthLook.StealthHiddenAlpha;
        else if (context.Entities.Views.TryGetValue(charId, out var who) && !who.Attackable) alpha = StealthSeenAlpha;
        else alpha = stealthDetected.Contains(charId) ? StealthSeenAlpha : StealthLook.StealthHiddenAlpha;

        StealthLook.StealthSetBodyAlpha(body, alpha);
        StealthLookChanged?.Invoke(charId, stealthed);

        if (charId == context.Self.CharId || !context.Entities.Views.TryGetValue(charId, out var ent)) return;
        ent.StealthUndetected = stealthed && !stealthDetected.Contains(charId);
        if (context.Selection.SelectedId == charId && StealthLook.HiddenFromMe(ent))
        {
            context.Selection.Deselect();
        }
    }

    private void StealthSpawnFx(int charId)
    {
        var body = StealthBodyOf(charId);
        if (body == null) return;
        var node = Fx.Spawn(StealthFx, body, new Vector3(0, 1.0f, 0));
        node?.After(StealthFxSeconds, node.QueueFree);
    }

    internal Node3D? StealthBodyOf(int charId)
    {
        if (charId == context.Self.CharId) return GodotObject.IsInstanceValid(context.Self.Body) ? context.Self.Body : null;
        if (context.Entities.Views.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
            return ent.Body;
        return null;
    }
}
