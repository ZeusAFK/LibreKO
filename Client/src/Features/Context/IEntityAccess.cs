using Godot;

namespace LibreKO.Features;

internal interface IEntityAccess
{
    Node3D? BodyOf(int charId);

    float HeadHeight(int charId);

    PlateStack? PlateOf(int charId);

    string NameOf(int charId);


    void SpawnFxOn(int charId, string fxName, float y);

    IEnumerable<EntityInfo> All();

    EntityInfo? Find(int charId);

    void RefreshNameColours();

    void SetClanMark(int clanId, int version, Texture2D? mark);

    IEnumerable<EntityCape> PlayerCapes();

    void SetCape(int charId, int capeId, int r, int g, int b);

    EntityRegistry Views { get; }

    void Redress(EntityView e, bool force = false);

    IReadOnlySet<int> StealthIds { get; }

    void RefreshClanLine(int charId);

    void RefreshSelfClanLine();

    Vector3? WorldPosOf(int charId);

    void RefreshPartyPlates();

    Node3D Layer { get; }

    void ReleaseCorpse(EntityView e);

    void GenieAura(int entityId, bool on);
}
