using Godot;

namespace LibreKO.Presentation;

internal static class SoundCues
{
    internal const int SoundSlots = 2;

    internal static bool MutedPlayer(EntityView e) => !Config.OtherPlayersSounds && !e.IsNpc && !e.IsMonster;

    internal static int PickVariant(int a, int b) => b != 0 && GD.Randf() < 0.5f ? b : a != 0 ? a : b;

    private static int HeldWeapon(int[]? gear)
    {
        if (gear == null) return 0;
        int right = gear.Length > InventoryConstants.VisRightHand ? gear[InventoryConstants.VisRightHand] : 0;
        if (right != 0) return right;
        return gear.Length > InventoryConstants.VisLeftHand ? gear[InventoryConstants.VisLeftHand] : 0;
    }

    internal static int SwingSound(int[]? gear, SoundCatalog.LooksSounds? looks)
    {
        int weapon = HeldWeapon(gear);
        return weapon != 0 ? SoundCatalog.ItemSwing(weapon) : looks?.Attack0 ?? 0;
    }

    internal static bool EntitySwinging(EntityView e, double now) =>
        e.ActionRank == ModelAnimation.ActionRankBasic && now < e.ActionUntil;

    internal static bool EntityStriking(EntityView e, double now) =>
        e.ActionRank is ModelAnimation.ActionRankBasic or ModelAnimation.ActionRankSkill && now < e.ActionUntil;

    private const float ImpactSoundPullback = 6.0f;

    internal static void AudioWeaponImpact(Vector3 from, Node3D target)
    {
        var away = target.GlobalPosition - from;
        float dist = away.Length();
        var at = dist <= ImpactSoundPullback
            ? from
            : from + away / dist * (dist - ImpactSoundPullback);
        Audio.PlayOwned(Sfx.WeaponImpact, at, target.GetInstanceId(), SoundOwnerKind.Impact);
    }

    internal readonly record struct AnimBeats(int Swings, int Strikes)
    {
        public bool None => Swings == 0 && Strikes == 0;
    }

    internal static AnimBeats AnimEventCrossings(AnimationPlayer? anim, ref string? clip, ref double prev)
    {
        if (anim == null) { clip = null; return default; }
        if (!CrowdAnimator.Current(anim, out string name, out double pos))
        {
            using var current = anim.CurrentAnimation;
            name = current.ToString();
            pos = name.Length == 0 ? 0 : anim.CurrentAnimationPosition;
        }
        if (name.Length == 0) { clip = null; return default; }
        if (name != clip) { clip = name; prev = pos; return default; }

        double last = prev;
        prev = pos;
        if (ModelAnimation.AnimationMetaFor(anim, name) is not { } meta) return default;

        int swings = 0, strikes = 0;
        for (int slot = 0; slot < SoundSlots; slot++)
        {
            if (Crossed(meta.SoundTime(slot), last, pos)) swings++;
            if (Crossed(meta.StrikeTime(slot), last, pos)) strikes++;
        }
        return new AnimBeats(swings, strikes);

        static bool Crossed(float at, double last, double pos) => at >= 0f && last < at && pos >= at;
    }
}
