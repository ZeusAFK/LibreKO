using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class GameMenuFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly CombatSession combat;
    private readonly SelfSession self;
    private readonly PetSession pet;
    private readonly IClientSettingsRequests settingsRequests;

    public GameMenuFeature(IWorldContext context, CombatSession combat, SelfSession self, PetSession pet, IClientSettingsRequests settingsRequests)
    {
        this.context = context;
        this.combat = combat;
        this.self = self;
        this.pet = pet;
        this.settingsRequests = settingsRequests;
    }

    public void Tick(double delta, double nowSec) => ExitTick(delta);

    internal void OpenSettings() => SettingsPanel.Open(context.Root, settingsRequests);

    public void Build()
    {
        BuildMenu();
        Game.Connection.WindowCloseHandler = OpenForWindowClose;
        combat.AttackEvent += OnExitBattleAttack;
    }

    public void Dispose()
    {
        if (Game.Connection.WindowCloseHandler == OpenForWindowClose) Game.Connection.WindowCloseHandler = null;
        combat.AttackEvent -= OnExitBattleAttack;
    }

    internal enum ExitKind { None, CharacterSelect, Quit }

    private const int ExitCharacterSelectText = 44641;
    private const int ExitEndGameText = 44642;
    private const int ExitOptionText = 44643;
    private const int ExitCancelText = 44430;
    private const int ExitCountdownText = 7632;
    private const int ExitCanceledText = 7633;
    private const int ExitInBattleText = 7634;
    private const double BattleIdle = -1.0;
    private const int ExitAfterBattleSeconds = 10;
    private const int TimedRowsShown = 3;
    private const float TimedRowHeight = 50f;
    private const int TimedRowGap = 4;
    private const float TimedIconSize = 40f;
    private static readonly Color ExitNoticeColour = new(1f, 0f, 0f);

    private Control core = null!;
    private VBoxContainer timed = null!;
    private VBoxContainer timedList = null!;
    private CanvasLayer? exitSkinLayer;
    private ExitKind exitArmed;
    private double battleTimer = BattleIdle;
    private int exitShownSeconds = ExitAfterBattleSeconds;

    private readonly record struct ExitRow(string Key, string Caption, Action Run, string Tooltip);

    private List<ExitRow> ExitMenuRows() =>
    [
        new("character_select", GameText.System(ExitCharacterSelectText, "Character Select"), () => RequestExit(ExitKind.CharacterSelect),
            "Return to character selection"),
        new("option", GameText.System(ExitOptionText, "Option"), () => { Toggle(false); OpenSettings(); },
            "Open graphics and game settings"),
        new("end_game", GameText.System(ExitEndGameText, "End Game"), () => RequestExit(ExitKind.Quit), "Close the game"),
        new("cancel", GameText.System(ExitCancelText, "Cancel"), () => Toggle(false), "Return to the world"),
    ];

    private VBoxContainer BuildTimedItems()
    {
        var box = new VBoxContainer { Visible = false };
        box.AddThemeConstantOverride("separation", 6);
        var notice = UiTheme.Text(GameText.System(TimedItems.TitleText, "Show all temporary items in the inventory and Magic Bag"),
            12, UiTheme.TextHi, HorizontalAlignment.Center);
        notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(notice);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, TimedRowsShown * (TimedRowHeight + TimedRowGap) - TimedRowGap),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        timedList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        timedList.AddThemeConstantOverride("separation", TimedRowGap);
        scroll.AddChild(timedList);
        box.AddChild(scroll);
        box.AddChild(UiTheme.Rule());
        return box;
    }

    private List<ExitMenuEntry> ExitTimedEntries()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entries = new List<ExitMenuEntry>();
        foreach (var item in TimedItems.Collect(self.Inventory.Length, abs => self.Inventory[abs], pet.Pet?.Items))
        {
            var left = TimedItems.Remaining(item.Slot.ExpiresAt, now);
            entries.Add(new ExitMenuEntry(item.Slot,
                ItemData.DisplayName(item.Slot.ItemId),
                TimedItems.PlaceText(item.Place) is { } place ? GameText.System(place, "") : "",
                GameText.System(TimedItems.TextId(left.Unit), "%d").Replace("%d", left.Amount.ToString()),
                left.Urgent));
        }
        return entries;
    }

    private readonly record struct ExitMenuEntry(ItemSlot Slot, string Name, string Place, string TimeLeft, bool Urgent);

    internal void RefreshExitMenu()
    {
        CloseExitMenuSkin();
        var entries = ExitTimedEntries();
        if (PluginHost.Ui.ExitMenuBuilder is { } build)
        {
            var rows = ExitMenuRows();
            var request = new LibreKO.Plugins.ExitMenuRequest(
                entries.Count > 0 ? GameText.System(TimedItems.TitleText, "Show all temporary items in the inventory and Magic Bag") : "",
                entries.ConvertAll(e => new LibreKO.Plugins.ExitMenuItem(e.Slot.ItemId, ItemIcons.Get(e.Slot.ItemId), e.Name, e.Place, e.TimeLeft, e.Urgent)),
                rows.ConvertAll(r => new LibreKO.Plugins.ExitMenuRow(r.Key, r.Caption, r.Run)),
                () => Toggle(false));
            core.Visible = false;
            exitSkinLayer = new CanvasLayer { Layer = UiLayers.ExitMenu };
            context.Root.AddChild(exitSkinLayer);
            exitSkinLayer.AddChild(build(request));
            return;
        }
        core.Visible = true;
        foreach (var child in timedList.GetChildren()) child.QueueFree();
        foreach (var entry in entries) timedList.AddChild(TimedRow(entry));
        timed.Visible = entries.Count > 0;
    }

    private Control TimedRow(ExitMenuEntry entry)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, TimedRowHeight) };
        row.AddThemeConstantOverride("separation", 10);
        var slot = new ItemSlotView(TimedIconSize) { Veiled = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        slot.Set(entry.Slot);
        slot.Hovered += view => context.Tooltips.Show(-1, view.Item);
        slot.Unhovered += _ => context.Tooltips.Hide();
        row.AddChild(slot);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", 0);
        var name = UiTheme.Text(entry.Name, 13, ItemGrade.Tint(entry.Slot.ItemId));
        name.ClipText = true;
        text.AddChild(name);
        text.AddChild(UiTheme.Text($"{entry.Place}  ·  {entry.TimeLeft}", 11, entry.Urgent ? TimedItemsFeature.UrgentColor : TimedItemsFeature.PlaceColor));
        row.AddChild(text);
        return row;
    }

    private void CloseExitMenuSkin()
    {
        if (exitSkinLayer != null && GodotObject.IsInstanceValid(exitSkinLayer)) exitSkinLayer.QueueFree();
        exitSkinLayer = null;
    }

    private void ExitNotice(string text) => context.Log.Add(text, CombatLogKind.Status, ExitNoticeColour);

    internal void RequestExit(ExitKind kind)
    {
        Toggle(false);
        if (battleTimer < 0)
        {
            RunExit(kind);
            return;
        }
        ExitNotice(GameText.System(ExitInBattleText, "You cannot exit from the client during a battle."));
        exitArmed = kind;
        exitShownSeconds = ExitAfterBattleSeconds;
    }

    private void RunExit(ExitKind kind)
    {
        if (kind == ExitKind.Quit)
        {
            OnExitGame();
            return;
        }
        Game.Connection.ReturnToCharSelect();
        context.Root.GetTree().ChangeSceneToFile("res://scenes/CharSelect.tscn");
    }

    private void OnExitBattleAttack(AttackReply reply)
    {
        if (reply.AttackerId != context.Self.CharId && reply.TargetId != context.Self.CharId) return;
        battleTimer = 0;
        if (exitArmed == ExitKind.None) return;
        exitArmed = ExitKind.None;
        exitShownSeconds = ExitAfterBattleSeconds;
        ExitNotice(GameText.System(ExitCanceledText, "Exiting game canceled."));
    }

    internal void ExitTick(double delta)
    {
        if (battleTimer >= 0)
        {
            if (battleTimer >= ExitAfterBattleSeconds) battleTimer = BattleIdle;
            else battleTimer += delta;
        }
        if (exitArmed == ExitKind.None) return;
        int left = battleTimer < 0 ? 0 : Math.Max(0, (int)(ExitAfterBattleSeconds - battleTimer));
        if (exitShownSeconds > left)
        {
            exitShownSeconds = left;
            ExitNotice(TextTemplate.Fill(GameText.System(ExitCountdownText, "Exiting game in %d seconds."), left));
        }
        if (exitShownSeconds != 0) return;
        var kind = exitArmed;
        exitArmed = ExitKind.None;
        RunExit(kind);
    }

    internal CanvasLayer Layer { get; private set; } = null!;

    private bool shown;

    internal bool IsOpen => shown;

    internal void BuildMenu()
    {
        Layer = new CanvasLayer { Layer = UiLayers.GameMenu, Visible = false };
        context.Root.AddChild(Layer);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.48f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Layer.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Layer.AddChild(center);
        core = center;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(4));
        center.AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        var header = new PanelContainer();
        header.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(4));
        root.AddChild(header);
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        header.AddChild(headerRow);
        var title = UiTheme.Text("Menu", 18, UiTheme.TextHi);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(title);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 12, 12, 12, 14);
        root.AddChild(margin);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        margin.AddChild(vb);

        timed = BuildTimedItems();
        vb.AddChild(timed);
        foreach (var row in ExitMenuRows())
            AddButton(vb, row.Caption, row.Run, row.Tooltip);
    }

    private static void AddButton(VBoxContainer vb, string text, System.Action onPressed, string tooltip = "")
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 42),
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.Pressed += onPressed;
        vb.AddChild(b);
    }

    private bool OpenForWindowClose()
    {
        if (!context.Ready || shown) return false;
        Toggle(true);
        return true;
    }

    internal void Toggle(bool? show = null)
    {
        shown = show ?? !shown;
        Layer.Visible = shown;
        if (shown) RefreshExitMenu();
        else CloseExitMenuSkin();
    }

    private void OnExitGame() => _ = Diag.Guard("world-exit", () => Shutdown.Begin(context.Root, 200));
}
