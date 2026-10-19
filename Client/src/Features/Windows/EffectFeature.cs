using Godot;

namespace LibreKO.Features.Windows;

internal sealed class EffectFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly EffectSession effect;
    private readonly UpgradeNoticeFeature upgradeNotice;

    public EffectFeature(IWorldContext context, EffectSession effect, UpgradeNoticeFeature upgradeNotice)
    {
        this.context = context;
        this.effect = effect;
        this.upgradeNotice = upgradeNotice;
    }

    private const int TextItemAnnouncement = 7681;
    private const int SparkleFx = 20100;
    internal const int GlowFx = 10106;
    private static readonly Color AnnouncementColour = new(1f, 1f, 0f);

    public void Build() => effect.EffectEvent += On;

    public void Dispose() => effect.EffectEvent -= On;

    private void On(EffectReply e)
    {
        if (e.EntityId == EffectWire.AnnouncementEntity)
        {
            if (e.Name.Length == 0 || e.Value == 0) return;
            upgradeNotice.ShowBanner(ResurrectOfferFeature.FillInOrder(
                GameText.System(TextItemAnnouncement, "Congratulation!! %s has received [%s] item."),
                e.Name, ItemData.DisplayName(e.Value)), AnnouncementColour);
            return;
        }

        int fx = e.Value switch
        {
            EffectWire.SparkleValue => SparkleFx,
            EffectWire.GlowValue => GlowFx,
            _ => 0,
        };
        if (fx != 0)
        {
            if (Fx.NameForId(fx) is { } name) context.Entities.SpawnFxOn(e.EntityId, name, 0f);
            return;
        }
        if (e.Value >= EffectWire.WindowValueFirst) return;
        if (SkillData.Get(e.Value)?.TargetFx is { } targetFx) context.Entities.SpawnFxOn(e.EntityId, targetFx, 0f);
    }
}
