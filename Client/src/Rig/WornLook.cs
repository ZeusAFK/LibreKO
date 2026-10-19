using Godot;

namespace LibreKO;

public static class WornLook
{
    public const string TransformNodeName = "TransformModel";
    public const string WornAccessoriesMeta = "worn_accessories";

    public static Node3D? Of(Node3D body)
    {
        if (body.Name == TransformNodeName) return body;
        if (body.GetNodeOrNull<Node3D>(TransformNodeName) is { } worn) return worn;
        return body.GetParent() is CharacterBody3D host ? host.GetNodeOrNull<Node3D>(TransformNodeName) : null;
    }

    public static (Node3D? Body, int Race) AccessoryHost(Node3D body, int race)
    {
        if (Of(body) is not { } worn) return (body, race);
        return worn.GetMeta(WornAccessoriesMeta, false).AsBool() ? (worn, PlayerRig.StandardRace(race)) : (null, race);
    }
}
