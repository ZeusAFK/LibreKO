using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private void GmFxInit()
    {
        Net.I.GmFxEvent += ApplyGmFx;
        ApplyGmFx(_myId, Net.I.GmFxVisible(_myId, _isGm));
    }

    private void GmFxDispose() => Net.I.GmFxEvent -= ApplyGmFx;

    private void ApplyGmFx(int id, bool enabled)
    {
        Node3D? body = null;
        bool isGm = false;
        if (id == _myId) { body = _self; isGm = _isGm; }
        else if (_ents.TryGetValue(id, out var ent)) { body = ent.Body; isGm = ent.IsGm; }
        if (body != null) SetGmAura(body, enabled && isGm);
    }

    internal static void SetGmAura(Node3D body, bool enabled)
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
        aura.Pin(body, FindFirst<Skeleton3D>(body) ?? body, Vector3.Zero, Vector3.Back);
    }
}
