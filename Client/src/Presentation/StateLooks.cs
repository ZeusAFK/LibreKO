using Godot;

namespace LibreKO.Presentation;

internal static class StateLooks
{
    internal static void ApplyEntityCombatStance(EntityView e, bool ready)
    {
        if (e.CombatStance == ready) return;
        e.CombatStance = ready;
        e.Clip = null;
    }

    internal const string TransformNodeName = WornLook.TransformNodeName;

    internal static string[] BoneNames(Skeleton3D skel)
    {
        var names = new string[skel.GetBoneCount()];
        for (int i = 0; i < names.Length; i++) names[i] = skel.GetBoneName(i);
        return names;
    }

    internal static void DressClanGauntlet(Node3D body, int race, int clanGrade, int clanRanking)
    {
        var (look, lookRace) = WornLook.AccessoryHost(body, race);
        if (look != body) ClanGauntletRig.AttachClanGauntlet(body, race, 0, 0);
        if (look != null) ClanGauntletRig.AttachClanGauntlet(look, lookRace, clanGrade, clanRanking);
    }

    internal static void SetOwnLookVisible(Node3D host, bool visible)
    {
        foreach (var child in host.GetChildren())
        {
            if (child is not Node3D node || node.Name == TransformNodeName) continue;
            if (node.FindChild(ModelAnimation.ModelNodeName, true, false) is Node3D model)
                model.Visible = visible;
            else if (node.Name == ModelAnimation.ModelNodeName)
                node.Visible = visible;
        }
    }
}
