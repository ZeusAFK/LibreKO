using Godot;
using LibreKO.Features.PluginBridge;

namespace LibreKO.Features.Hud;

internal sealed class HpStatusFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly PartySession party;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly StatusHudFeature statusHud;
    private readonly TargetHudFeature targetHud;
    private readonly PluginBridgeFeature pluginBridge;

    public HpStatusFeature(IWorldContext context, PartySession party, ZoneAbilitySession zoneAbility, StatusHudFeature statusHud, TargetHudFeature targetHud, PluginBridgeFeature pluginBridge)
    {
        this.context = context;
        this.party = party;
        this.zoneAbility = zoneAbility;
        this.statusHud = statusHud;
        this.targetHud = targetHud;
        this.pluginBridge = pluginBridge;
    }

    private const byte StatusBlind = 4;

    private const int BlindFx = 30011;

    private const double HpStatusCycleSeconds = 0.5;

    private Godot.Timer? hpStatusTick;

    private Node3D? blindFx;

    public void Build()
    {
        zoneAbility.OwnStatusEvent += OnOwnStatus;
        hpStatusTick = new Godot.Timer { WaitTime = HpStatusCycleSeconds, Autostart = true };
        hpStatusTick.Timeout += RefreshHpStatusBars;
        context.Root.AddChild(hpStatusTick);
    }

    public void Dispose()
    {
        zoneAbility.OwnStatusEvent -= OnOwnStatus;
        ClearBlindFx();
    }

    private void OnOwnStatus(byte type, bool inflicted)
    {
        if (type == StatusBlind)
        {
            if (inflicted) ShowBlindFx(); else ClearBlindFx();
            return;
        }
        RefreshHpStatusBars();
    }

    private void RefreshHpStatusBars()
    {
        long second = (long)context.Now;
        if (statusHud.HpBar != null && GodotObject.IsInstanceValid(statusHud.HpBar))
            statusHud.HpBar.FillOverride = StatusFill(zoneAbility.OwnStatus, second);
        if (targetHud.TargetHp != null && GodotObject.IsInstanceValid(targetHud.TargetHp))
            targetHud.TargetHp.FillOverride = targetHud.TargetHudId >= 0 ? StatusFill(party.StatusOf(targetHud.TargetHudId), second) : null;
        pluginBridge.NotifyTarget();
    }

    private static Color? StatusFill(System.Collections.Generic.IReadOnlyCollection<byte>? statuses, long second) =>
        PartyHud.HpLook(statuses, second) is var look and not PartyHpLook.Normal ? PartyFeature.HpFills[look] : null;

    private void ShowBlindFx()
    {
        if (blindFx != null && GodotObject.IsInstanceValid(blindFx)) return;
        if (Fx.NameForId(BlindFx) is not { } name) return;
        blindFx = Fx.Spawn(name, context.Self.Body, Vector3.Zero);
    }

    private void ClearBlindFx()
    {
        if (blindFx != null && GodotObject.IsInstanceValid(blindFx)) blindFx.QueueFree();
        blindFx = null;
    }
}
