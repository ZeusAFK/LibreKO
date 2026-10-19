using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class ReactionBar : PopupPanel
{
    private const int Cell = 34;
    private const int EmojiSize = 20;
    private const int PlusSize = 18;
    private const int Gap = 2;
    private const int Lift = 6;
    private static readonly Color PanelFill = new(0.13f, 0.135f, 0.15f, 0.98f);
    private static readonly Color Edge = new(0.32f, 0.31f, 0.34f, 0.9f);
    private static readonly Color Hover = new(1f, 1f, 1f, 0.1f);
    private static readonly Color Chosen = new(1f, 1f, 1f, 0.18f);

    private readonly List<(string Emoji, Button Button)> picks = [];

    public event Action<string>? Picked;
    public event Action? More;

    public ReactionBar()
    {
        var panel = new StyleBoxFlat { BgColor = PanelFill, BorderColor = Edge };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(Cell / 2 + 4);
        panel.SetContentMarginAll(4);
        AddThemeStyleboxOverride("panel", panel);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Gap);
        AddChild(row);
        foreach (string emoji in ReactionRules.QuickPicks)
        {
            var button = Cellular(emoji, EmojiSize);
            string picked = emoji;
            button.Pressed += () =>
            {
                Hide();
                Picked?.Invoke(picked);
            };
            row.AddChild(button);
            picks.Add((emoji, button));
        }
        var plus = Cellular("+", PlusSize);
        plus.TooltipText = "More emoji and emoticons";
        plus.AddThemeColorOverride("font_color", UiTheme.TextLo);
        plus.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
        plus.Pressed += () =>
        {
            Hide();
            More?.Invoke();
        };
        row.AddChild(plus);
    }

    private static Button Cellular(string text, int size)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(Cell, Cell),
        };
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", Round(Hover));
        button.AddThemeStyleboxOverride("pressed", Round(Hover));
        Audio.HookButton(button);
        return button;
    }

    private static StyleBoxFlat Round(Color fill)
    {
        var style = new StyleBoxFlat { BgColor = fill };
        style.SetCornerRadiusAll(Cell / 2);
        return style;
    }

    public void ShowAbove(Rect2 anchor, string current)
    {
        foreach (var (emoji, button) in picks)
            button.AddThemeStyleboxOverride("normal", emoji == current ? Round(Chosen) : new StyleBoxEmpty());
        ResetSize();
        var room = GetTree().Root.GetVisibleRect().Size;
        float x = anchor.GetCenter().X - Size.X / 2f;
        float y = anchor.Position.Y - Size.Y - Lift;
        if (y < 0) y = anchor.End.Y + Lift;
        Position = new Vector2I(
            (int)Mathf.Clamp(x, 0, Mathf.Max(0, room.X - Size.X)),
            (int)Mathf.Clamp(y, 0, Mathf.Max(0, room.Y - Size.Y)));
        Popup();
    }
}

public sealed partial class ReactionDetails : PopupPanel
{
    private const float Width = 260f;
    private const float ListMaxHeight = 228f;
    private const int RowHeight = 38;
    private const int Avatar = 28;
    private const int TitleSize = 13;
    private const int NameSize = 13;
    private const int HintSize = 10;
    private const int ChipGlyphSize = 14;
    private const int RowGlyphSize = 18;
    private const int Lift = 6;
    private static readonly Color PanelFill = new(0.12f, 0.125f, 0.135f, 0.99f);
    private static readonly Color Edge = new(0.32f, 0.31f, 0.34f, 0.9f);
    private static readonly Color ChipFill = new(1f, 1f, 1f, 0.05f);
    private static readonly Color ChipChosen = new(1f, 1f, 1f, 0.16f);
    private static readonly Color ChipEdge = new(1f, 1f, 1f, 0.14f);
    private static readonly Color Divider = new(1f, 1f, 1f, 0.08f);
    private static readonly Color RowHover = new(1f, 1f, 1f, 0.06f);

    private readonly Label title;
    private readonly HBoxContainer chips;
    private readonly VBoxContainer list;
    private readonly ScrollContainer scroll;
    private string filter = "";
    private int self;

    public RoomPost? Post { get; private set; }

    public event Action? AddRequested;
    public event Action? RemoveMine;

    public ReactionDetails()
    {
        var panel = new StyleBoxFlat { BgColor = PanelFill, BorderColor = Edge };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(8);
        panel.ContentMarginLeft = panel.ContentMarginRight = 10;
        panel.ContentMarginTop = 8;
        panel.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", panel);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Width, 0) };
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);
        title = UiTheme.Text("", TitleSize, UiTheme.TextLo);
        column.AddChild(title);
        chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 6);
        column.AddChild(chips);
        column.AddChild(new ColorRect { Color = Divider, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        column.AddChild(scroll);
        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(list);
    }

    public void ShowFor(RoomPost post, int self, Rect2 anchor)
    {
        Post = post;
        this.self = self;
        filter = "";
        Fill(post);
        ResetSize();
        var room = GetTree().Root.GetVisibleRect().Size;
        float x = anchor.Position.X;
        float y = anchor.Position.Y - Size.Y - Lift;
        if (y < 0) y = anchor.End.Y + Lift;
        Position = new Vector2I(
            (int)Mathf.Clamp(x, 0, Mathf.Max(0, room.X - Size.X)),
            (int)Mathf.Clamp(y, 0, Mathf.Max(0, room.Y - Size.Y)));
        Popup();
    }

    public void Refresh()
    {
        if (Post is not { } post || !Visible) return;
        if (post.Reactions.Total == 0)
        {
            Hide();
            return;
        }
        Fill(post);
        ResetSize();
    }

    private void Fill(RoomPost post)
    {
        var reactions = post.Reactions;
        var tally = reactions.Tally();
        if (filter.Length > 0 && !tally.Any(each => each.Emoji == filter)) filter = "";
        title.Text = ReactionRules.Title(reactions.Total);
        Empty(chips);
        var add = UiTheme.IconButton(UiIcons.Get("system/smiley"), "Add a reaction");
        add.CustomMinimumSize = new Vector2(30, 26);
        add.AddThemeStyleboxOverride("normal", ChipStyle(false));
        add.AddThemeStyleboxOverride("hover", ChipStyle(true));
        add.AddThemeStyleboxOverride("pressed", ChipStyle(true));
        add.AddThemeColorOverride("icon_normal_color", UiTheme.TextLo);
        add.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
        add.Pressed += () =>
        {
            Hide();
            AddRequested?.Invoke();
        };
        chips.AddChild(add);
        foreach (var each in tally) chips.AddChild(Chip(each));

        Empty(list);
        foreach (var reaction in reactions.Listed(filter, self)) list.AddChild(Row(reaction));
        int rows = list.GetChildCount();
        scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(ListMaxHeight, rows * RowHeight));
    }

    private Control Chip(ReactionCount count)
    {
        bool chosen = filter == count.Emoji;
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        chip.AddThemeStyleboxOverride("panel", ChipStyle(chosen));
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        chip.AddChild(line);
        line.AddChild(ChatThreadView.Glyph(count.Emoji, ChipGlyphSize));
        var number = UiTheme.Text(count.Count.ToString(), 12, UiTheme.TextHi);
        number.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(number);
        string emoji = count.Emoji;
        chip.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            chip.AcceptEvent();
            filter = filter == emoji ? "" : emoji;
            if (Post != null) Callable.From(() => { Fill(Post); ResetSize(); }).CallDeferred();
        };
        return chip;
    }

    private static StyleBoxFlat ChipStyle(bool chosen)
    {
        var style = new StyleBoxFlat { BgColor = chosen ? ChipChosen : ChipFill, BorderColor = ChipEdge };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(13);
        style.ContentMarginLeft = style.ContentMarginRight = 9;
        style.ContentMarginTop = style.ContentMarginBottom = 3;
        return style;
    }

    private Control Row(Reaction reaction)
    {
        bool mine = reaction.CharacterId == self;
        var row = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, RowHeight),
            MouseFilter = mine ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);
        line.AddChild(AvatarOf(reaction));
        var names = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        names.AddThemeConstantOverride("separation", 0);
        line.AddChild(names);
        var name = UiTheme.Text(mine ? "You" : reaction.Name, NameSize, UiTheme.TextHi);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(name);
        if (mine)
        {
            var hint = UiTheme.Text("Click to remove", HintSize, UiTheme.TextDim);
            hint.MouseFilter = Control.MouseFilterEnum.Ignore;
            names.AddChild(hint);
        }
        var glyph = ChatThreadView.Glyph(reaction.Emoji, RowGlyphSize);
        line.AddChild(glyph);
        if (!mine) return row;
        row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        var hover = new StyleBoxFlat { BgColor = RowHover };
        hover.SetCornerRadiusAll(4);
        row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hover);
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            row.AcceptEvent();
            RemoveMine?.Invoke();
        };
        return row;
    }

    private static Control AvatarOf(Reaction reaction)
    {
        var colour = new Color(RoomNameColours.Of(reaction.CharacterId));
        var circle = new PanelContainer
        {
            CustomMinimumSize = new Vector2(Avatar, Avatar),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat { BgColor = colour.Darkened(0.55f), BorderColor = colour.Darkened(0.2f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(Avatar / 2);
        circle.AddThemeStyleboxOverride("panel", style);
        string initial = reaction.Name.Length > 0 ? reaction.Name[..1].ToUpperInvariant() : "?";
        var letter = UiTheme.Text(initial, 13, colour, HorizontalAlignment.Center);
        letter.VerticalAlignment = VerticalAlignment.Center;
        letter.MouseFilter = Control.MouseFilterEnum.Ignore;
        circle.AddChild(letter);
        return circle;
    }

    private static void Empty(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
