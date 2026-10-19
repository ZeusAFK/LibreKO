using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Controls;

internal sealed class HotkeysFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;

    public HotkeysFeature(IWorldContext context, WorldRoot world)
    {
        this.context = context;
        this.world = world;
    }

    public void LateTick(double delta, double nowSec) => TickPadTriggers();

    public void Build()
    {
        Rebind();
        KeyBinds.Changed += Rebind;
    }

    public void Dispose() => KeyBinds.Changed -= Rebind;

    private readonly Dictionary<KeyChord, Action> hotkeys = new();
    private readonly Dictionary<PadChord, Action> padHotkeys = new();

    private readonly Dictionary<KeyChord, Action> commander = new();

    private readonly Dictionary<KeyAction, Action> keyActions = new();

    internal void RunKeyAction(KeyAction action)
    {
        if (keyActions.TryGetValue(action, out var handler)) handler();
    }

    internal bool TryPress(InputEventKey key)
    {
        var chord = KeyChord.From(key);
        if (KeyBinds.Held(KeyAction.CommanderHold) && commander.TryGetValue(chord, out var command))
        {
            command();
            return true;
        }
        if (!hotkeys.TryGetValue(chord, out var press)) return false;
        press();
        return true;
    }

    internal bool TryPressPad(JoyButton button)
    {
        if (!padHotkeys.TryGetValue(new PadChord(button, KeyBinds.ActiveMod()), out var press)) return false;
        press();
        return true;
    }

    private void Bound(KeyAction action, Action handler)
    {
        keyActions[action] = handler;
        var chord = KeyBinds.Get(action);
        if (chord.Assigned) hotkeys[chord] = handler;
        var pad = KeyBinds.GetPad(action);
        if (pad.Assigned) padHotkeys[pad] = handler;
    }

    private bool leftTriggerHeld;
    private bool rightTriggerHeld;

    internal void TickPadTriggers()
    {
        if (!KeyBinds.PadConnected) return;
        FireOnTriggerEdge(PadMod.LeftTrigger, ref leftTriggerHeld);
        FireOnTriggerEdge(PadMod.RightTrigger, ref rightTriggerHeld);
    }

    private void FireOnTriggerEdge(PadMod mod, ref bool held)
    {
        bool down = KeyBinds.TriggerHeld(mod);
        if (down && !held
            && padHotkeys.TryGetValue(new PadChord(JoyButton.Invalid, mod), out var press))
            press();
        held = down;
    }

    private void WorldBound(KeyAction action, Action handler) => Bound(action, () => { if (!world.Exchange.TradeHoldsInput) handler(); });

    private void DevHotkey(Key key, Action handler) => hotkeys[new KeyChord(key, false, false, false)] = handler;

    private void Rebind()
    {
        hotkeys.Clear();
        padHotkeys.Clear();
        commander.Clear();
        keyActions.Clear();

        WorldBound(KeyAction.AutoRun, world.PlayerSystem.ToggleAutoRun);
        WorldBound(KeyAction.ToggleRun, world.PlayerSystem.ToggleRunMode);
        WorldBound(KeyAction.ToggleRunAlt, () => { if (!world.PartyCommander.AmCommander) world.PlayerSystem.ToggleRunMode(); });
        WorldBound(KeyAction.Sit, () => world.PlayerSystem.ToggleSitting());
        WorldBound(KeyAction.TargetHostile, world.PlayerInput.TargetNearestHostile);
        WorldBound(KeyAction.TargetFriendly, () => world.Selection.SelectNearest(hostile: false));
        WorldBound(KeyAction.TargetParty, world.Selection.SelectNearestPartyMember);
        WorldBound(KeyAction.AutoAttack, () => world.BasicAttackSystem.ToggleAutoAttack());
        WorldBound(KeyAction.StealthCancel, () => world.StealthSystem.StealthCancelSelf());

        for (int i = 0; i < HotbarFeature.HotSlotsPerPage; i++)
        {
            int slot = i;
            WorldBound(KeyAction.HotSlot1 + i, () => world.Hotbar.ActivateHotSlot(slot));
        }
        for (int i = 0; i < HotbarFeature.HotPages; i++)
        {
            int page = i;
            Bound(KeyAction.HotPage1 + i, () => world.Hotbar.SetHotPage(page));
        }
        Bound(KeyAction.HotPageNext, () => world.Hotbar.ChangeHotPage(1));
        for (int i = 0; i < CommanderOrders.Bar.Length; i++)
        {
            int slot = i;
            var chord = KeyBinds.Get(KeyAction.CommanderSlot1 + i);
            if (chord.Assigned) commander[chord] = () => { if (!world.Exchange.TradeHoldsInput) world.PartyCommander.PressCommanderKey(slot); };
        }

        WorldBound(KeyAction.PotionHp, () => world.PotionBar.UsePotion(world.PotionBar.HpPotion));
        WorldBound(KeyAction.PotionMp, () => world.PotionBar.UsePotion(world.PotionBar.MpPotion));
        WorldBound(KeyAction.CameraTurn, world.CameraSystem.StartCameraHalfTurn);
        Bound(KeyAction.GameMenu, world.HandleEscape);

        Bound(KeyAction.Character, () => world.MainWindows.Toggle("Character"));
        Bound(KeyAction.Inventory, () => world.MainWindows.Toggle("Inventory"));
        Bound(KeyAction.Skills, () =>
        {
            if (!world.ChaosSkillWindow.ToggleChaosSkillPage()) world.MainWindows.Toggle("Skills");
        });
        Bound(KeyAction.Quests, () => world.MainWindows.Toggle("Quests"));
        Bound(KeyAction.Party, () => world.SeekParty.ToggleSeek());
        Bound(KeyAction.NamePlates, () => world.EntityPlatesSystem.ToggleNamePlates());
        Bound(KeyAction.Friends, () => world.MainWindows.OpenCharacterPage(CharacterPage.Friends));
        Bound(KeyAction.Messenger, () => world.Messenger.Toggle());
        Bound(KeyAction.MiniMap, () => world.Map.ToggleMini());
        Bound(KeyAction.ZoneMap, () => world.Map.ToggleFull());
        Bound(KeyAction.WorldMap, () => world.GlobalMap.Toggle());
        Bound(KeyAction.Helmet, () => world.Cosmetics.ToggleHelmet());
        WorldBound(KeyAction.Interact, () =>
        {
            if (world.GatherSystem.TryStartGather() || world.Exchange.TradeHoldsInput) return;
            world.PlayerSystem.PressInteract();
        });
        Bound(KeyAction.MyShop, () => world.Merchant.ToggleMenu());
        Bound(KeyAction.Pet, () => world.Pet.Toggle());
        Bound(KeyAction.PowerUpStore, world.ShoppingMall.Toggle);
        Bound(KeyAction.ClanWarehouse, () => world.ClanWarehouse.Toggle());
        Bound(KeyAction.VipWarehouse, () => world.VipWarehouse.Toggle());
        Bound(KeyAction.Achievements, () => world.Achievement.ToggleAchievements());
        Bound(KeyAction.CommandList, world.CommandList.Toggle);
        Bound(KeyAction.Mail, () => world.Mail.Toggle());
        Bound(KeyAction.Lottery, () => world.Lottery.Toggle());
        Bound(KeyAction.Attendance, () => world.Attendance.Toggle());
        Bound(KeyAction.Bounty, () => world.Bounty.Toggle());
        Bound(KeyAction.Tournament, () => world.Tournament.Toggle());
        Bound(KeyAction.Presets, () => world.SkillPreset.TogglePreset());
        Bound(KeyAction.NationForce, () => world.Forces.Toggle());
        Bound(KeyAction.ChatRooms, () => world.NationChannels.ToggleNation());
        Bound(KeyAction.Roulette, () => world.Roulette.Toggle());
        Bound(KeyAction.FishingHall, () => world.FishingHall.Toggle());
        Bound(KeyAction.Genie, () =>
        {
            if (context.Selection.SelectedId >= 0 && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target) && target.IsMonster) world.Genie.AddTarget();
            else world.Genie.Toggle();
        });
        Bound(KeyAction.TownRecall, () => world.TownRecall.TryOpen());
        Bound(KeyAction.GmPanel, world.AdminPanel.Toggle);

        if (Config.Development)
        {
            DevHotkey(Key.H, world.DebugInfo.TogglePanel);
            DevHotkey(Key.V, () => world.ScenerySystem.PreviewNextFx());
            hotkeys[new KeyChord(Key.J, true, false, false)] = PreviewFloaters;
        }
    }

    internal void PreviewFloaters()
    {
        context.Floaters.Wound(63);
        context.Floaters.Cure(context.Self.CharId, 240);
        context.Floaters.RegenHp(37);
        context.Floaters.RegenMp(21);
        context.Floaters.Exp(4820);
        context.Floaters.Gold(1350);
        context.Floaters.Item(379006000, 3);
        context.Floaters.Notice("You have no Water of Favors left.");
        int victim = context.Selection.SelectedId;
        if (victim < 0)
            foreach (var (id, _) in context.Entities.Views) { victim = id; break; }
        if (victim >= 0)
            foreach (int hit in new[] { 148, 96, 1207 })
                context.Floaters.Damage(victim, hit);
    }
}
