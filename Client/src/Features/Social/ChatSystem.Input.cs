using Godot;

namespace LibreKO.Features.Social;

internal sealed partial class ChatSystem
{
    private const int CounterFontSize = 11;
    private const float CounterInset = 6f;
    private const float IdleInputAlpha = 0.85f;
    private const string IdleHint = "Press Enter to chat";
    private const string TypingHint = "@name whisper · ! shout · # party · $ clan · & alliance · ↑ last lines";
    private static readonly Color CounterColour = new("a8a298");
    private static readonly Color CounterFullColour = new("e06666");
    private static readonly string[] ReplyPrefixes = { "/r ", "/reply " };

    private Label counterLabel = null!;
    private StyleBox? inputRoomy;
    private StyleBox? inputActive;
    private TextureButton emoticonButton = null!;
    private StyleBox? inputBare;
    private readonly ChatInputHistory history = new();
    private readonly ChatLinkDraft linkDraft = new();
    private string lastInputText = "";

    private void BuildInputRow()
    {
        inputRow = new HBoxContainer();
        inputRow.AddThemeConstantOverride("separation", 6);
        if (Platform.TouchUi)
        {
            root.AddChild(inputRow);
        }
        else
        {
            logBody.AddChild(inputRow);
            inputRow.AnchorLeft = 0;
            inputRow.AnchorRight = 1;
            inputRow.AnchorTop = 1;
            inputRow.AnchorBottom = 1;
            inputRow.OffsetRight = -HudLogText.ScrollbarGutter;
            inputRow.GrowVertical = Control.GrowDirection.Begin;
            inputRow.Resized += ReserveInputRoom;
        }

        input = new LineEdit
        {
            MaxLength = ChatInputText.MaxLength,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = TypingHint,
        };
        input.TextSubmitted += Submit;
        input.TextChanged += OnInputChanged;
        input.GuiInput += OnInputKey;
        input.FocusExited += () => Callable.From(Suspend).CallDeferred();
        input.TreeEntered += () =>
        {
            inputBare = null;
            inputRoomy = null;
            inputActive = null;
            ApplyInputStyle();
        };
        inputRow.AddChild(input);

        emoticonButton = EmoticonPicker.InputButton(EmoticonButtonSize, EmoticonInset);
        emoticonButton.Pressed += OpenEmoticons;
        input.AddChild(emoticonButton);

        counterLabel = UiTheme.Text("", CounterFontSize, CounterColour);
        counterLabel.Visible = false;
        counterLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        counterLabel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        counterLabel.GrowHorizontal = Control.GrowDirection.Begin;
        counterLabel.GrowVertical = Control.GrowDirection.Both;
        counterLabel.OffsetRight = -(CounterInset + EmoticonRoom);
        input.AddChild(counterLabel);
    }

    private bool GameMasterMode => gmFx.Visible(self.LastEnter.CharId, self.IsGm);

    private const int EmoticonButtonSize = 18;
    private const float EmoticonInset = 4f;
    private const float EmoticonRoom = EmoticonButtonSize + EmoticonInset * 2;
    private EmoticonPicker? emoticons;

    private bool EmoticonsOpen => emoticons != null && emoticons.Visible;

    private void OpenEmoticons()
    {
        if (emoticons == null)
        {
            emoticons = new EmoticonPicker();
            context.Root.AddChild(emoticons);
            emoticons.Picked += code =>
            {
                if (EmoticonPicker.Insert(input, code) is { } refusal) Info(refusal);
                Open();
            };
            emoticons.PopupHide += () => Callable.From(() => { if (!input.HasFocus()) Suspend(); }).CallDeferred();
        }
        emoticons.ShowAt(inputRow.GlobalPosition);
    }

    private void UpdateInputColour()
    {
        var channel = (ChatType)ChatPrefixes.ChannelFor(input.Text, sendChannel);
        input.AddThemeColorOverride("font_color", colors.ForInput(channel, GameMasterMode, self.LastEnter.Nation));
    }

    private void OnInputKey(InputEvent ev)
    {
        if (!active && ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Open();
            input.AcceptEvent();
            return;
        }
        if (ev is not InputEventKey { Pressed: true } key) return;
        if (IsPaste(key))
        {
            PasteClipboard();
            input.AcceptEvent();
            return;
        }
        if (key.CtrlPressed || key.AltPressed || key.MetaPressed) return;
        switch (key.Keycode)
        {
            case Key.Up:
                if (history.Older(input.Text) is { } older) ShowTyped(older);
                break;
            case Key.Down:
                if (history.Newer() is { } newer) ShowTyped(newer);
                break;
            case Key.Pageup:
                ScrollLog(-1);
                break;
            case Key.Pagedown:
                ScrollLog(1);
                break;
            default:
                return;
        }
        input.AcceptEvent();
    }

    private static bool IsPaste(InputEventKey key) =>
        (key.Keycode == Key.V && (key.CtrlPressed || key.MetaPressed) && !key.AltPressed)
        || (key.Keycode == Key.Insert && key.ShiftPressed);

    private void PasteClipboard()
    {
        string clean = ChatInputText.Paste(DisplayServer.ClipboardGet());
        if (clean.Length == 0) return;
        string text = input.Text;
        int fromColumn = input.HasSelection() ? input.GetSelectionFromColumn() : input.CaretColumn;
        int toColumn = input.HasSelection() ? input.GetSelectionToColumn() : input.CaretColumn;
        int from = TextRunes.Utf16Index(text, fromColumn);
        int to = TextRunes.Utf16Index(text, toColumn);
        string merged = text[..from] + clean + text[to..];
        if (TextRunes.Count(merged) > input.MaxLength)
        {
            int room = Math.Max(0, input.MaxLength - TextRunes.Count(text[..from] + text[to..]));
            clean = TextRunes.Truncate(clean, room);
            merged = text[..from] + clean + text[to..];
        }
        input.Text = merged;
        input.CaretColumn = fromColumn + TextRunes.Count(clean);
        OnInputChanged(merged);
    }

    private void ShowTyped(string text)
    {
        input.Text = text;
        input.CaretColumn = text.Length;
        OnInputChanged(text);
    }

    private void OnInputChanged(string text)
    {
        if (lastWhisperFrom.Length > 0 && Array.Exists(ReplyPrefixes, p => string.Equals(text, p, StringComparison.OrdinalIgnoreCase)))
        {
            ShowTyped($"@{lastWhisperFrom} ");
            return;
        }
        linkDraft.TextChanged(lastInputText, text);
        lastInputText = text;
        ApplyInputLimit();
        UpdateCounter();
        UpdateInputColour();
    }

    private void ApplyInputLimit()
    {
        int limit = ChatInputText.MaxLength - linkDraft.WireExtra;
        if (input.MaxLength == limit) return;
        int caret = input.CaretColumn;
        input.MaxLength = limit;
        input.CaretColumn = caret;
    }

    private void UpdateCounter()
    {
        int length = TextRunes.Count(input.Text) + linkDraft.WireExtra;
        string? counter = active ? ChatInputText.Counter(length, ChatInputText.MaxLength) : null;
        if (counterLabel.Visible != (counter != null))
        {
            counterLabel.Visible = counter != null;
            ApplyInputStyle();
        }
        if (counter == null) return;
        counterLabel.Text = counter;
        counterLabel.AddThemeColorOverride("font_color", length >= ChatInputText.MaxLength ? CounterFullColour : CounterColour);
    }

    private void ApplyInputStyle()
    {
        bool idle = !active && !Platform.TouchUi;
        input.PlaceholderText = idle ? IdleHint : TypingHint;
        input.RemoveThemeStyleboxOverride("normal");
        input.RemoveThemeStyleboxOverride("read_only");
        var normal = input.GetThemeStylebox("normal");
        if (idle)
        {
            var bare = inputBare ?? BareStyle(normal);
            if (input.IsInsideTree()) inputBare = bare;
            input.AddThemeStyleboxOverride("normal", bare);
            input.AddThemeStyleboxOverride("read_only", bare);
            return;
        }
        if (!counterLabel.Visible)
        {
            if (inputActive == null)
            {
                inputActive = (StyleBox)normal.Duplicate();
                inputActive.ContentMarginRight = normal.GetMargin(Side.Right) + EmoticonRoom;
            }
            input.AddThemeStyleboxOverride("normal", inputActive);
            return;
        }
        if (inputRoomy == null)
        {
            inputRoomy = (StyleBox)normal.Duplicate();
            string counterText = counterLabel.Text;
            counterLabel.Text = ChatInputText.Counter(ChatInputText.MaxLength, ChatInputText.MaxLength);
            inputRoomy.ContentMarginRight = normal.GetMargin(Side.Right) + counterLabel.GetCombinedMinimumSize().X + CounterInset + EmoticonRoom;
            counterLabel.Text = counterText;
        }
        input.AddThemeStyleboxOverride("normal", inputRoomy);
    }

    private static StyleBox BareStyle(StyleBox normal)
    {
        var bare = new StyleBoxEmpty();
        foreach (Side side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
            bare.SetContentMargin(side, normal.GetMargin(side));
        return bare;
    }

    private void ReserveInputRoom()
    {
        float room = inputRow.Size.Y + LogBodyGap;
        scroll.ReserveBottom(room);
        jumpPill.OffsetBottom = -(room + PillLift);
    }

    internal bool InsertItemLink(int itemId)
    {
        if (!active || ItemData.Get(itemId) == null) return false;
        var (text, caret) = linkDraft.Insert(input.Text, TextRunes.Utf16Index(input.Text, input.CaretColumn), itemId, ItemData.DisplayName(itemId));
        if (text == input.Text) return true;
        if (TextRunes.Count(text) + linkDraft.WireExtra > ChatInputText.MaxLength)
        {
            linkDraft.Clear();
            return true;
        }
        ApplyInputLimit();
        input.Text = text;
        input.CaretColumn = TextRunes.Column(text, caret);
        lastInputText = text;
        UpdateCounter();
        input.GrabFocus();
        return true;
    }
}
