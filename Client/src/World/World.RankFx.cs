using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string RankAuraNode = "rank_fx";
    private const string RankAuraRetiredNode = "rank_fx_retired";
    private static readonly StringName RankAuraFxMeta = "rank_fx_name";

    private int _selfPersonalRank = NationRankAura.Unranked;

    private void RefreshRankAura(Node3D? body, int personalRank, bool stealthed)
    {
        if (body == null || !GodotObject.IsInstanceValid(body)) return;
        string? want = null;
        if (!stealthed && !NationRankAura.HiddenIn(_zone))
        {
            int fxId = NationRankAura.FxIdFor(personalRank);
            if (fxId != NationRankAura.NoFx) want = Fx.NameForId(fxId);
        }
        var current = body.GetNodeOrNull<Node3D>(RankAuraNode);
        string? have = current != null && current.HasMeta(RankAuraFxMeta) ? current.GetMeta(RankAuraFxMeta).AsString() : null;
        if (want == have) return;
        if (current != null)
        {
            current.Name = RankAuraRetiredNode;
            Fx.Free(current);
        }
        if (want == null) return;
        var aura = Fx.Spawn(want, body, Vector3.Zero, oneShot: false);
        if (aura == null) return;
        aura.Name = RankAuraNode;
        aura.SetMeta(RankAuraFxMeta, want);
    }

    private void RefreshRankAuraFor(int charId, bool stealthed)
    {
        if (charId == _myId) RefreshRankAura(_self, _selfPersonalRank, stealthed);
        else if (_ents.TryGetValue(charId, out var e) && !e.IsNpc) RefreshRankAura(e.Body, e.PersonalRank, stealthed);
    }
}
