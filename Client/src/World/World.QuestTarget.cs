using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int TargetMapSide = 260;

    private HudWindow? _questTargetWindow;
    private TargetMapView? _questTargetMap;
    private VBoxContainer? _questTargetText;
    private Label? _questTargetCaption;

    private HudWindow? _questReceiptWindow;

    private void OnQuestTarget(QuestTargetDetail target)
    {
        EnsureQuestTargetWindow();
        _questTargetWindow!.Title = target.QuestTitle.Length > 0 ? target.QuestTitle : target.Target;

        foreach (var child in _questTargetText!.GetChildren()) { _questTargetText.RemoveChild(child); child.QueueFree(); }
        var name = target.About.Length > 0 ? target.About.Split('\n', 2)[0] : target.Target;
        var lore = target.About.Contains('\n') ? target.About.Split('\n', 2)[1] : "";
        var heading = UiTheme.Text(name, 15, UiTheme.GoldBright);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.CustomMinimumSize = new Vector2(TargetMapSide, 0);
        _questTargetText.AddChild(heading);
        if (lore.Length > 0)
            _questTargetText.AddChild(QuestParagraph(lore, UiTheme.TextLo, TargetMapSide));

        _questTargetCaption!.Text = target.Where.Length > 0 ? target.Where : MapName(target.ZoneId);
        _questTargetMap!.SetTarget(
            target.ZoneId == _zone ? _miniMap?.MapTexture : null,
            _miniMap?.MapExtent ?? 0f,
            target.ZoneId == _zone && target.HasCoordinates ? new Vector2(target.X, target.Z) : null,
            target.ZoneId == _zone ? "" : $"In {MapName(target.ZoneId)}");
        _questTargetWindow.Visible = true;
    }

    private void EnsureQuestTargetWindow()
    {
        if (_questTargetWindow != null) return;
        var layer = new CanvasLayer { Layer = 75 };
        AddChild(layer);
        _questTargetWindow = new HudWindow("quest_target", "Target", new Vector2(320, 150), TargetMapSide) { Visible = false };
        layer.AddChild(_questTargetWindow);

        var body = _questTargetWindow.Body;
        body.AddThemeConstantOverride("separation", 8);

        var textPanel = UiTheme.Section();
        body.AddChild(textPanel);
        _questTargetText = new VBoxContainer();
        _questTargetText.AddThemeConstantOverride("separation", 6);
        textPanel.AddChild(_questTargetText);

        _questTargetCaption = UiTheme.Text("", 13, UiTheme.Gold, HorizontalAlignment.Center);
        _questTargetCaption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _questTargetCaption.CustomMinimumSize = new Vector2(TargetMapSide, 0);
        body.AddChild(_questTargetCaption);

        _questTargetMap = new TargetMapView
        {
            CustomMinimumSize = new Vector2(TargetMapSide, TargetMapSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        body.AddChild(_questTargetMap);

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += () => _questTargetWindow.Visible = false;
        body.AddChild(close);
    }

    private void OnQuestReceipt(QuestReceipt receipt)
    {
        if (_questReceiptWindow == null)
        {
            var layer = new CanvasLayer { Layer = 76 };
            AddChild(layer);
            _questReceiptWindow = new HudWindow("quest_receipt", "Reward", new Vector2(480, 220), 300) { Visible = false };
            layer.AddChild(_questReceiptWindow);
        }

        var body = _questReceiptWindow.Body;
        foreach (var child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
        body.AddThemeConstantOverride("separation", 8);
        body.AddChild(UiTheme.Text(
            _questStrings.TryGetValue(receipt.QuestId, out var strings) ? strings.Title : "Quest complete",
            13, UiTheme.Gold));

        var rewards = UiTheme.Section();
        body.AddChild(rewards);
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        rewards.AddChild(list);
        foreach (var entry in receipt.Granted)
            list.AddChild(QuestItemRow(entry.ItemId, ItemData.DisplayName(entry.ItemId),
                entry.Count.ToString("n0"), UiTheme.GoldBright));

        var confirm = new Button { Text = "Confirm", CustomMinimumSize = new Vector2(0, 36) };
        confirm.Pressed += () => { HideItemTooltip(); _questReceiptWindow.Visible = false; };
        body.AddChild(confirm);
        _questReceiptWindow.Visible = true;
    }

    private sealed partial class TargetMapView : Control
    {
        private Texture2D? _tex;
        private float _extent;
        private Vector2? _target;
        private string _note = "";

        public void SetTarget(Texture2D? tex, float extent, Vector2? target, string note)
        {
            _tex = tex; _extent = extent; _target = target; _note = note;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var side = Mathf.Min(Size.X, Size.Y);
            if (side <= 0f) return;
            var rect = new Rect2((Size - new Vector2(side, side)) * 0.5f, new Vector2(side, side));

            if (_tex != null)
                DrawTextureRect(_tex, rect, false);
            else
                DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));
            DrawRect(rect, new Color(UiTheme.Gold, 0.85f), false, 2f);

            if (_note.Length > 0)
            {
                var font = ThemeDB.FallbackFont;
                var width = font.GetStringSize(_note, fontSize: 13).X;
                DrawString(font, rect.Position + new Vector2((side - width) * 0.5f, side * 0.5f),
                    _note, HorizontalAlignment.Left, -1, 13, UiTheme.TextLo);
                return;
            }

            if (_target is not { } target || _extent <= 0f) return;
            float scale = side / _extent;
            var point = rect.Position + new Vector2(target.X * scale, side - target.Y * scale);
            DrawCircle(point, 11, new Color(0, 0, 0, 0.8f));
            DrawArc(point, 9, 0, Mathf.Tau, 24, UiTheme.GoldBright, 3, true);
            DrawLine(point - new Vector2(15, 0), point + new Vector2(15, 0), UiTheme.GoldBright, 2, true);
            DrawLine(point - new Vector2(0, 15), point + new Vector2(0, 15), UiTheme.GoldBright, 2, true);
        }
    }
}
