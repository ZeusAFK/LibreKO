using Godot;

namespace LibreKO.Features.Hud;

internal sealed class PartyCommanderFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly SkillTimers skillTimers;
    private readonly IPartyRequests requests;
    private readonly PartyFeature partyHud;

    public PartyCommanderFeature(IWorldContext context, ClanSession clan, PartySession party, SelfSession self, SkillTimers skillTimers, IPartyRequests requests, PartyFeature partyHud)
    {
        this.context = context;
        this.clan = clan;
        this.party = party;
        this.self = self;
        this.skillTimers = skillTimers;
        this.requests = requests;
        this.partyHud = partyHud;
    }

    public void Tick(double delta, double nowSec)
    {
        CommanderTick(nowSec);
        CommanderFrame(delta);
    }

    internal bool UserHidden => commanderUserHidden;

    internal void SetUserHidden(bool hidden)
    {
        commanderUserHidden = hidden;
        RefreshCommanderBar();
    }

    private const float CommanderButtonSize = 40f;
    private const float CommanderIconInset = 3f;
    private const int CommanderCooldownFont = 13;
    private const float CommanderBadgeSize = 88f;
    private const float CommanderGlyphSize = 66f;
    private const float CommanderGlyphInset = 11f;
    private const float CommanderRingSize = 86f;
    private const float CommanderRingInset = 1f;
    private const float CommanderSweepSize = 64f;
    private const float CommanderSweepInset = 12f;
    private const float CommanderPinSize = 50f;
    private const float CommanderPinLeft = 25f;
    private const float CommanderPinUp = 75f;
    private const float CommanderPinClanUp = 17f;
    private const float CommanderSheetCell = 68f;
    private const float CommanderSheetEdge = 2f;
    private const float CommanderRingEdge = 2f;
    private const float CommanderRingImage = 124f;
    private const float CommanderAlphaScale = 255f;
    private const string CommanderDir = "res://assets/ui/commander/";
    private const string CommanderMarkTexture = CommanderDir + "target.png";
    private const string CommanderFlashRed = CommanderDir + "flash_red.png";
    private const string CommanderFlashBlue = CommanderDir + "flash_blue.png";
    private const string CommanderBadgeSheet = CommanderDir + "badge.png";
    private static readonly Rect2 CommanderPlateRegion = new(138f, 2f, 88f, 88f);
    internal static readonly Color CommanderNameColour = new(0f, 1f, 0f);
    private static readonly Color CommanderNoTargetColour = new("ff3b3b");

    internal HudWindow? CommanderBar { get; private set; }
    private readonly Button[] commanderButtons = new Button[CommanderOrders.Bar.Length];
    private readonly Label[] commanderCooldowns = new Label[CommanderOrders.Bar.Length];
    private BoxContainer commanderSlots = null!;
    private bool commanderUserHidden;
    private TextureRect commanderPin = null!;
    private int commanderMarkId = -1;
    private double commanderMarkSentAt;
    internal CanvasLayer CommanderAlarm { get; private set; } = null!;
    private TextureRect commanderFlash = null!;
    internal Control CommanderBadge { get; private set; } = null!;
    private TextureRect commanderGlyph = null!;
    private TextureRect commanderRing = null!;
    private TextureRect commanderSweep = null!;
    internal CommanderPulse CommanderPulse { get; } = new();
    private Texture2D[] commanderRingBlue = System.Array.Empty<Texture2D>();
    private Texture2D[] commanderRingRed = System.Array.Empty<Texture2D>();
    private Texture2D[] commanderSweepFrames = System.Array.Empty<Texture2D>();
    private bool commanderRedRing;

    internal bool AmCommander => party.InParty && party.PartyCommanderId == context.Self.CharId;

    public void Build()
    {
        partyHud.Attach(this);
        BuildCommanderBar();
        BuildCommanderAlarm();
        party.PartyCommanderEvent += OnCommander;
        party.CommanderMarkEvent += OnCommanderMark;
        party.CommanderOrderEvent += OnCommanderOrder;
        party.PartyDisbandEvent += OnCommanderGone;
        party.PartyRemovedEvent += OnCommanderMemberGone;
        RefreshCommanderBar();
    }

    public void Dispose()
    {
        partyHud.Detach(this);
        party.PartyCommanderEvent -= OnCommander;
        party.CommanderMarkEvent -= OnCommanderMark;
        party.CommanderOrderEvent -= OnCommanderOrder;
        party.PartyDisbandEvent -= OnCommanderGone;
        party.PartyRemovedEvent -= OnCommanderMemberGone;
    }

    internal void BuildCommanderBar()
    {
        CommanderBar = new HudWindow("commander", "Commander") { Visible = false };
        CommanderBar.Closed += () =>
        {
            commanderUserHidden = true;
            CommanderBar.Visible = false;
            RefreshCommanderBar();
        };
        var layer = new CanvasLayer { Layer = UiLayers.Notices };
        context.Root.AddChild(layer);
        layer.AddChild(CommanderBar);
        var flip = UiTheme.SmallButton("\u21c4", "Change the bar direction");
        flip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        flip.Pressed += FlipCommanderBar;
        CommanderBar.Body.AddChild(flip);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        CommanderBar.Body.AddChild(row);
        commanderSlots = row;
        for (int i = 0; i < CommanderOrders.Bar.Length; i++)
        {
            int slot = i;
            var skill = SkillData.Get(CommanderOrders.Bar[i].Skill);
            var button = new Button
            {
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(CommanderButtonSize, CommanderButtonSize),
                TooltipText = CommanderTooltip(skill, slot),
            };
            var icon = new TextureRect
            {
                Texture = SkillIcons.Get(CommanderOrders.Bar[i].Skill),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            icon.OffsetLeft = icon.OffsetTop = CommanderIconInset;
            icon.OffsetRight = icon.OffsetBottom = -CommanderIconInset;
            button.AddChild(icon);
            button.Pressed += () => UseCommanderSlot(slot);
            var cooldown = UiTheme.Text("", CommanderCooldownFont, UiTheme.TextHi, HorizontalAlignment.Center);
            cooldown.VerticalAlignment = VerticalAlignment.Center;
            cooldown.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            cooldown.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.AddChild(cooldown);
            commanderButtons[i] = button;
            commanderCooldowns[i] = cooldown;
            row.AddChild(button);
        }
    }

    private void FlipCommanderBar()
    {
        var parent = commanderSlots.GetParent();
        BoxContainer flipped = commanderSlots is HBoxContainer ? new VBoxContainer() : new HBoxContainer();
        flipped.AddThemeConstantOverride("separation", 4);
        foreach (var button in commanderButtons)
            button.Reparent(flipped);
        parent.AddChild(flipped);
        commanderSlots.QueueFree();
        commanderSlots = flipped;
        CommanderBar?.ResetSize();
    }

    private static string CommanderTooltip(SkillData.Skill? skill, int slot)
    {
        string key = KeyBinds.Get(KeyAction.CommanderSlot1 + slot).Text;
        string name = skill?.Name ?? "";
        string desc = skill?.Desc ?? "";
        return desc.Length > 0 ? $"{name}\n{desc}\n{key}" : $"{name}\n{key}";
    }

    internal void BuildCommanderAlarm()
    {
        CommanderAlarm = new CanvasLayer { Layer = UiLayers.CommanderAlarm };
        context.Root.AddChild(CommanderAlarm);
        commanderFlash = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        commanderFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        CommanderAlarm.AddChild(commanderFlash);

        var sheet = ResourceLoader.Exists(CommanderBadgeSheet) ? GD.Load<Texture2D>(CommanderBadgeSheet) : null;
        CommanderBadge = new Control
        {
            CustomMinimumSize = new Vector2(CommanderBadgeSize, CommanderBadgeSize),
            Size = new Vector2(CommanderBadgeSize, CommanderBadgeSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        CommanderAlarm.AddChild(CommanderBadge);
        CommanderBadge.AddChild(CommanderImage(sheet == null ? null : new AtlasTexture { Atlas = sheet, Region = CommanderPlateRegion },
            0f, CommanderBadgeSize));
        commanderGlyph = CommanderImage(null, CommanderGlyphInset, CommanderGlyphSize);
        CommanderBadge.AddChild(commanderGlyph);
        commanderRing = CommanderImage(null, CommanderRingInset, CommanderRingSize);
        commanderRing.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        CommanderBadge.AddChild(commanderRing);
        commanderSweep = CommanderImage(null, CommanderSweepInset, CommanderSweepSize);
        commanderSweep.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        CommanderBadge.AddChild(commanderSweep);
        commanderRingBlue = CommanderFrames("ring_blue_", CommanderPulse.RingImages, true);
        commanderRingRed = CommanderFrames("ring_red_", CommanderPulse.RingImages, true);
        commanderSweepFrames = CommanderFrames("sweep_", CommanderPulse.SweepImages, false);

        commanderPin = new TextureRect
        {
            Texture = ResourceLoader.Exists(CommanderMarkTexture) ? GD.Load<Texture2D>(CommanderMarkTexture) : null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = new Vector2(CommanderPinSize, CommanderPinSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        CommanderAlarm.AddChild(commanderPin);
    }

    private static TextureRect CommanderImage(Texture2D? texture, float inset, float size) => new()
    {
        Texture = texture,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        Position = new Vector2(inset, inset),
        Size = new Vector2(size, size),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static Texture2D[] CommanderFrames(string stem, int count, bool trimEdge)
    {
        var frames = new Texture2D[count];
        for (int i = 0; i < count; i++)
        {
            string path = CommanderDir + stem + i + ".png";
            var image = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            frames[i] = image != null && trimEdge
                ? new AtlasTexture { Atlas = image, Region = new Rect2(CommanderRingEdge, CommanderRingEdge, CommanderRingImage, CommanderRingImage) }
                : image!;
        }
        return frames;
    }

    private static AtlasTexture? CommanderGlyphTexture(Texture2D? sheet, int glyph)
    {
        if (sheet == null || glyph < 0) return null;
        int column = glyph % 2, row = glyph / 2;
        return new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(CommanderSheetEdge + column * CommanderSheetCell, CommanderSheetEdge + row * CommanderSheetCell,
                CommanderGlyphSize, CommanderGlyphSize),
        };
    }

    private void PlaceCommanderBadge()
    {
        if (!context.Root.IsInsideTree()) return;
        var screen = context.Root.GetViewport().GetVisibleRect().Size;
        CommanderBadge.Position = screen * 0.5f;
    }

    internal bool CommanderBarHidden => AmCommander && commanderUserHidden;

    private void RefreshCommanderBar()
    {
        partyHud.RefreshConfig();
        context.Plugins.Party();
        if (CommanderBar == null) return;
        CommanderBar.Visible = AmCommander && !commanderUserHidden;
    }

    internal void ShowCommanderBarAgain()
    {
        commanderUserHidden = false;
        RefreshCommanderBar();
    }

    private void OnCommander(CommanderNamedReply reply)
    {
        if (reply.CharId == context.Self.CharId) commanderUserHidden = false;
        context.Log.Notice(reply.CharId == context.Self.CharId
            ? GameText.System(CommanderOrders.YouText, "Now, you are the commander")
            : TextTemplate.Fill(GameText.System(CommanderOrders.NamedText, "Now, %s was a commander"), reply.Name));
        ClearCommanderMark();
        RefreshCommanderBar();
        partyHud.RefreshHud();
    }

    private void OnCommanderGone()
    {
        ClearCommanderMark();
        RefreshCommanderBar();
    }

    private void OnCommanderMemberGone(int charId, string name)
    {
        if (charId == context.Self.CharId || party.PartyCommanderId < 0) ClearCommanderMark();
        RefreshCommanderBar();
    }

    private void OnCommanderMark(CommanderMarkReply reply)
    {
        if (reply.Clear) { ClearCommanderMark(); return; }
        PlaceCommanderMark(reply.TargetId);
        Audio.PlayUi(CommanderOrders.MarkSound);
    }

    private void PlaceCommanderMark(int targetId)
    {
        commanderMarkId = targetId;
    }

    private void ClearCommanderMark()
    {
        commanderPin.Visible = false;
        commanderMarkId = -1;
        commanderMarkSentAt = 0;
    }

    private void UpdateCommanderPin()
    {
        if (commanderMarkId < 0 || context.Camera == null)
        {
            commanderPin.Visible = false;
            return;
        }
        context.Entities.Views.TryGetValue(commanderMarkId, out var e);
        Node3D? host = commanderMarkId == context.Self.CharId ? context.Self.Body : e?.Body;
        bool hidden = e != null && (e.StealthUndetected || e.Infiltrating);
        if (host == null || hidden || host.GlobalPosition.DistanceTo(context.Self.Body.GlobalPosition) > CommanderOrders.PinReach)
        {
            commanderPin.Visible = false;
            return;
        }
        var head = host.GlobalPosition + new Vector3(0f, context.Entities.HeadHeight(commanderMarkId), 0f);
        if (context.Camera.IsPositionBehind(head))
        {
            commanderPin.Visible = false;
            return;
        }
        var point = context.Camera.UnprojectPosition(head);
        bool inClan = e != null ? e.KnightsId > 0 : clan.MyClan.InClan;
        commanderPin.Position = point - new Vector2(CommanderPinLeft, CommanderPinUp + (inClan ? CommanderPinClanUp : 0f));
        commanderPin.Visible = true;
    }

    internal void OnCommanderOrder(byte orderCode)
    {
        int glyph = CommanderOrders.Glyph((CommanderOrder)orderCode);
        if (glyph == CommanderOrders.NoGlyph) return;
        commanderFlash.Texture = GD.Load<Texture2D>(CommanderOrders.RedFlash((CommanderOrder)orderCode) ? CommanderFlashRed : CommanderFlashBlue);
        var sheet = ResourceLoader.Exists(CommanderBadgeSheet) ? GD.Load<Texture2D>(CommanderBadgeSheet) : null;
        commanderGlyph.Texture = CommanderGlyphTexture(sheet, CommanderOrders.SheetCell((CommanderOrder)orderCode));
        commanderRedRing = CommanderOrders.RedRing(glyph);
        CommanderPulse.Start();
        PlaceCommanderBadge();
        commanderFlash.Visible = true;
        CommanderBadge.Visible = true;
        ShowCommanderPulse();
        if (CommanderOrders.Sound((CommanderOrder)orderCode) is var sound and > 0) Audio.PlayUi(sound);
    }

    internal void ShowCommanderPulse()
    {
        commanderFlash.Modulate = new Color(1f, 1f, 1f, CommanderPulse.Alpha / CommanderAlphaScale);
        var ring = commanderRedRing ? commanderRingRed : commanderRingBlue;
        commanderRing.Texture = ring.Length > 0 ? ring[CommanderPulse.RingImage] : null;
        commanderSweep.Visible = CommanderPulse.Sweeping;
        commanderSweep.Texture = commanderSweepFrames.Length > 0 ? commanderSweepFrames[CommanderPulse.SweepImage] : null;
    }

    internal void CommanderFrame(double delta)
    {
        UpdateCommanderPin();
        if (!CommanderPulse.Running) return;
        CommanderPulse.Frame(delta);
        if (CommanderPulse.Running)
        {
            ShowCommanderPulse();
            return;
        }
        commanderFlash.Visible = false;
        CommanderBadge.Visible = false;
    }

    internal void PressCommanderKey(int slot)
    {
        context.Log.Notice(GameText.System(CommanderOrders.KeyHintText,
            "You have entered the pet or commander related shortcut key (Caps Lock or ALT)."), new Color(1f, 1f, 0f));
        UseCommanderSlot(slot);
    }

    internal void UseCommanderSlot(int slot)
    {
        if (!AmCommander || slot < 0 || slot >= CommanderOrders.Bar.Length) return;
        var entry = CommanderOrders.Bar[slot];
        var skill = SkillData.Get(entry.Skill);
        if (skill == null || skillTimers.OnCooldown(skill, context.Now)) return;
        if (entry.Order == CommanderOrder.None)
        {
            if (context.Selection.SelectedId < 0)
            {
                context.Log.Add(GameText.System(CommanderOrders.NoTargetText, "No target."), CombatLogKind.Status, CommanderNoTargetColour);
                return;
            }
            bool clear = CommanderOrders.ClearsMark(commanderMarkId, context.Selection.SelectedId);
            requests.CommanderMark(context.Selection.SelectedId, clear);
            commanderMarkSentAt = clear ? 0 : context.Now;
            skillTimers.Start(skill, context.Now);
            return;
        }
        requests.CommanderOrder(entry.Order);
        foreach (var other in CommanderOrders.Bar)
            if (other.Order != CommanderOrder.None && SkillData.Get(other.Skill) is { } orderSkill)
                skillTimers.Start(orderSkill, context.Now);
    }

    internal void CommanderTick(double now)
    {
        if (CommanderBar is not { Visible: true }) return;
        for (int i = 0; i < CommanderOrders.Bar.Length; i++)
        {
            var skill = SkillData.Get(CommanderOrders.Bar[i].Skill);
            double left = skill != null && self.SkillCooldowns.TryGetValue(skill.Id, out double until) ? until - now : 0;
            commanderButtons[i].Disabled = left > 0;
            commanderCooldowns[i].Text = left > 0 ? Mathf.CeilToInt((float)left).ToString() : "";
        }
        if (commanderMarkId >= 0 && commanderMarkSentAt > 0 && now - commanderMarkSentAt >= CommanderOrders.MarkSeconds)
        {
            requests.CommanderMark(commanderMarkId, true);
            commanderMarkSentAt = 0;
        }
    }
}
