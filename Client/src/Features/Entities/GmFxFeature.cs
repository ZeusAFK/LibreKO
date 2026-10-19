using Godot;

namespace LibreKO.Features.Entities;

internal sealed class GmFxFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly GmFxSession gmFx;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntitySystem entities;

    public GmFxFeature(IWorldContext context, GmFxSession gmFx, EntityLooksSystem entityLooks, EntitySystem entities)
    {
        this.context = context;
        this.gmFx = gmFx;
        this.entityLooks = entityLooks;
        this.entities = entities;
    }

    public void Build()
    {
        entities.EntitySpawned += OnEntitySpawned;
        gmFx.GmFxEvent += Apply;
        Apply(context.Self.CharId, gmFx.Visible(context.Self.CharId, context.Self.IsGm));
    }

    public void Dispose()
    {
        entities.EntitySpawned -= OnEntitySpawned;
        gmFx.GmFxEvent -= Apply;
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info) => Apply(e.Id, gmFx.Visible(e.Id, info.IsGm));

    internal void Apply(int id, bool enabled)
    {
        if (id == context.Self.CharId)
        {
            if (context.Self.Body != null) SetAura(context.Self.Body, enabled && context.Self.IsGm);
            entityLooks.DressSelf();
            return;
        }

        if (!context.Entities.Views.TryGetValue(id, out var ent)) return;
        ent.IsGm = enabled;
        if (ent.Body == null) return;
        SetAura(ent.Body, enabled);
        entityLooks.Dress(ent.Body, ent.CapeId, ent.CapeR, ent.CapeG, ent.CapeB, ent.IsGm, ent.Race);
    }

    internal static void SetAura(Node3D body, bool enabled)
    {
        const string name = "gm_aura";
        var current = body.GetNodeOrNull<Node3D>(name);
        if (current != null)
        {
            if (enabled) return;
            body.RemoveChild(current);
            current.QueueFree();
        }
        if (!enabled || Fx.NameForId(32160) is not { } fxName) return;
        if (Fx.Spawn(fxName, body, Vector3.Zero) is not FxInstance aura) return;
        aura.Name = name;
        aura.Pin(body, NodeSearch.FindFirst<Skeleton3D>(body) ?? body, Vector3.Zero, Vector3.Back);
    }
}
