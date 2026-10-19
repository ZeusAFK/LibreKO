using System;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public sealed partial class ChatComposer : HBoxContainer
{
    private const int EmoticonButtonSize = 20;
    private const float EmoticonInset = 4f;
    private const double HoldSeconds = 0.4;
    private const double TickSeconds = 0.1;
    private const int Gap = 6;
    private const string RecordTip = "Hold to record a voice note, or click to start and click again to send";
    private static readonly Color RecordingColour = new("e0574a");

    private readonly string target;
    private readonly ChatLinkDraft draft = new();
    private readonly HBoxContainer field;
    private readonly HBoxContainer strip;
    private readonly Label clock;
    private readonly ProgressBar levelBar;
    private readonly Button record;
    private readonly Button send;
    private EmoticonPicker? emoticons;
    private string lastText = "";
    private bool hold;
    private double pressedAt;
    private double tick;
    private VoiceChat? voice;

    public LineEdit Input { get; }

    public event Action<string>? Submitted;
    public event Action<string>? Notice;

    public ChatComposer(string target)
    {
        this.target = target;
        AddThemeConstantOverride("separation", Gap);

        field = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(field);
        Input = new LineEdit
        {
            PlaceholderText = "Message",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = ChatInputText.MaxLength,
            KeepEditingOnTextSubmit = true,
        };
        Input.TextSubmitted += _ => Submit();
        Input.TextChanged += text =>
        {
            draft.TextChanged(lastText, text);
            lastText = text;
            Limit();
        };
        var emoticonsButton = EmoticonPicker.InputButton(EmoticonButtonSize, EmoticonInset);
        emoticonsButton.Pressed += () => OpenEmoticons(emoticonsButton.GlobalPosition);
        Input.AddChild(emoticonsButton);
        Input.TreeEntered += () =>
        {
            foreach (string state in new[] { "normal", "focus" })
            {
                var roomy = (StyleBox)Input.GetThemeStylebox(state).Duplicate();
                roomy.ContentMarginRight += EmoticonButtonSize + EmoticonInset * 2;
                Input.AddThemeStyleboxOverride(state, roomy);
            }
        };
        field.AddChild(Input);

        strip = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
        strip.AddThemeConstantOverride("separation", Gap);
        AddChild(strip);
        strip.AddChild(UiTheme.Text("●", 14, RecordingColour));
        clock = UiTheme.Text("0:00", 13, UiTheme.TextHi);
        strip.AddChild(clock);
        levelBar = Meter(RecordingColour);
        strip.AddChild(levelBar);
        var cancel = UiTheme.IconButton(UiIcons.Get("system/close"), "Cancel the voice note");
        cancel.Pressed += () => VoiceChat.I?.StopRecording(send: false);
        strip.AddChild(cancel);

        record = UiTheme.IconButton(UiIcons.Get("system/waveform"), RecordTip);
        record.ButtonDown += OnRecordDown;
        record.ButtonUp += OnRecordUp;
        AddChild(record);

        send = UiTheme.IconButton(UiIcons.Get("system/send"), "Send");
        send.Pressed += Submit;
        AddChild(send);
    }

    public static ProgressBar Meter(Color tint)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.01,
            ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 6),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        bar.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(tint));
        return bar;
    }

    public override void _EnterTree()
    {
        voice = VoiceChat.I;
        if (voice != null) voice.StateChanged += RefreshRecording;
        RefreshRecording();
    }

    public override void _ExitTree()
    {
        if (voice != null) voice.StateChanged -= RefreshRecording;
        voice = null;
    }

    public override void _Process(double delta)
    {
        if (VoiceChat.I is not { } voice || !voice.RecordingFor(target)) return;
        tick += delta;
        if (tick < TickSeconds) return;
        tick = 0;
        clock.Text = $"{VoiceNoteRules.Clock(voice.RecordingSeconds)} / {VoiceNoteRules.Clock(VoiceNoteRules.MaxSeconds)}";
        levelBar.Value = voice.MicLevel;
    }

    public void RefreshRecording()
    {
        bool recording = VoiceChat.I?.RecordingFor(target) == true;
        field.Visible = !recording;
        strip.Visible = recording;
        send.Visible = !recording;
        record.Icon = UiIcons.Get(recording ? "system/send" : "system/waveform");
        record.TooltipText = recording ? "Send the voice note" : RecordTip;
        record.AddThemeColorOverride("icon_normal_color", recording ? RecordingColour : UiTheme.Gold);
        if (!recording) levelBar.Value = 0;
    }

    public void ShowRecording(double seconds, float level)
    {
        field.Visible = false;
        strip.Visible = true;
        send.Visible = false;
        record.Icon = UiIcons.Get("system/send");
        record.AddThemeColorOverride("icon_normal_color", RecordingColour);
        clock.Text = $"{VoiceNoteRules.Clock(seconds)} / {VoiceNoteRules.Clock(VoiceNoteRules.MaxSeconds)}";
        levelBar.Value = level;
    }

    public bool InsertItemLink(int itemId)
    {
        if (ItemData.Get(itemId) == null) return false;
        var (text, caret) = draft.Insert(Input.Text, TextRunes.Utf16Index(Input.Text, Input.CaretColumn), itemId, ItemData.DisplayName(itemId));
        if (TextRunes.Count(text) + draft.WireExtra > ChatInputText.MaxLength)
        {
            draft.Clear();
            return true;
        }
        Input.Text = text;
        Input.CaretColumn = TextRunes.Column(text, caret);
        lastText = text;
        Limit();
        Input.GrabFocus();
        return true;
    }

    private void Submit()
    {
        string message = Input.Text.Trim();
        if (message.Length == 0) return;
        string wire = draft.ToWire(message);
        draft.Clear();
        lastText = "";
        Input.Clear();
        Limit();
        if (!Input.HasFocus()) Input.CallDeferred(Control.MethodName.GrabFocus);
        Submitted?.Invoke(wire);
    }

    private void Limit()
    {
        int limit = ChatInputText.MaxLength - draft.WireExtra;
        if (Input.MaxLength == limit) return;
        int caret = Input.CaretColumn;
        Input.MaxLength = limit;
        Input.CaretColumn = caret;
    }

    private void OpenEmoticons(Vector2 at)
    {
        if (emoticons == null)
        {
            emoticons = new EmoticonPicker();
            AddChild(emoticons);
            emoticons.Picked += code =>
            {
                if (EmoticonPicker.Insert(Input, code) is { } refusal) Notice?.Invoke(refusal);
                Input.CallDeferred(Control.MethodName.GrabFocus);
            };
        }
        emoticons.ShowAt(at);
    }

    private void OnRecordDown()
    {
        if (VoiceChat.I is not { } voice) return;
        if (voice.RecordingFor(target))
        {
            hold = false;
            voice.StopRecording(send: true);
            return;
        }
        if (!voice.StartRecording(target)) return;
        hold = true;
        pressedAt = FrameClock.Now();
    }

    private void OnRecordUp()
    {
        if (VoiceChat.I is not { } voice || !voice.RecordingFor(target) || !hold) return;
        hold = false;
        if (FrameClock.Now() - pressedAt >= HoldSeconds) voice.StopRecording(send: true);
    }
}
