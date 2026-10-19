using Godot;

namespace LibreKO.Features;

internal enum CharacterPage { Character, Clan, Friends }

internal enum BagFit { Normal, Unfit, Staged }

internal sealed record BagCompanion(
    Func<int, bool> Take,
    Func<int, BagFit> Fit,
    Func<int, string> Note,
    Action Close,
    Func<int, int, int?, bool>? IntoBag = null);

internal readonly record struct EntityInfo(int Id, int NpcId, int NpcType, bool IsNpc, bool IsMonster, bool Attackable, bool Dead, bool Statue, int Nation, string Name, Vector3 Position);

internal readonly record struct EntityCape(int Id, Node3D Body, int CapeId, int R, int G, int B, bool IsGm, int Race, int KnightsId);

internal interface INpcDialog
{
    void Begin(int npcId, int textId);

    void Begin(string title, string body);

    void End(int shownCount);

    void Close();

    void AddMenuButton(string label, Action onPressed, string? icon = null, string tag = "", bool ready = false);

    void ShowServiceChoice(string body, params (string Label, Action Act)[] options);

    void TrackTalker(int npcCharId);

    void Balloon(string[] lines);

    void Show();
}

internal interface IMainWindows
{
    bool IsOpen(string key);

    void Show(string key);

    void Hide(string key);

    void Toggle(string key);

    bool CharTabOpen();

    void OpenCharacterPage(CharacterPage page);

    HudWindow? Window(string key);

    HudWindow? SkillsWindow();
}

internal interface IBagCompanions
{
    SlotHold Hold { get; }

    BagCompanion? Current { get; }

    void Attach(BagCompanion companion);

    void Detach(BagCompanion companion);

    void RefreshFit();

    void ReleaseHold();

    bool RefuseItemInUse(int abs, int other = -1);

    bool CompanionInto(int from, int to, int? amount);

    void ApplyFit();

    bool IsStagedSlot(int abs);
}
