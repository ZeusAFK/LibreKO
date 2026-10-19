using Godot;

namespace LibreKO.Features.Windows;

internal sealed class QuestReceiptFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npc;
    private readonly QuestBook book;

    public QuestReceiptFeature(IWorldContext context, NpcQuestSession npcQuest, QuestBook questBook)
    {
        this.context = context;
        npc = npcQuest;
        book = questBook;
    }

    public void Build()
    {
        npc.QuestReceiptEvent += OnReceipt;
    }

    public void Dispose()
    {
        npc.QuestReceiptEvent -= OnReceipt;
    }

    private const int ReceiptWidth = 300;
    private const float ReceiptCheckSide = 16f;

    internal HudWindow? ReceiptWindow { get; private set; }

    internal void OnReceipt(QuestReceipt receipt)
    {
        if (ReceiptWindow == null)
        {
            var layer = new CanvasLayer { Layer = UiLayers.Prompts };
            context.Root.AddChild(layer);
            ReceiptWindow = new HudWindow("quest_receipt", "Reward", ReceiptWidth) { Visible = false };
            layer.AddChild(ReceiptWindow);
        }

        var body = ReceiptWindow.Body;
        foreach (var child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
        body.AddThemeConstantOverride("separation", 8);
        var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        heading.AddThemeConstantOverride("separation", 6);
        heading.AddChild(new CheckGlyph(ReceiptCheckSide, UiTheme.Good));
        heading.AddChild(UiTheme.Text(
            book.Strings.TryGetValue(receipt.QuestId, out var strings) ? strings.Title : "Quest complete",
            13, UiTheme.Gold));
        body.AddChild(heading);

        var rewards = UiTheme.Section();
        body.AddChild(rewards);
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        rewards.AddChild(list);
        foreach (var entry in receipt.Granted)
            list.AddChild(QuestRows.ItemRow(context.Tooltips, entry.ItemId, ItemData.DisplayName(entry.ItemId),
                entry.Count.ToString("n0"), UiTheme.GoldBright));

        var confirm = new Button { Text = "Confirm", CustomMinimumSize = new Vector2(0, 36) };
        confirm.Pressed += () => { context.Tooltips.Hide(); ReceiptWindow.Visible = false; };
        body.AddChild(confirm);
        ReceiptWindow.Visible = true;
    }
}
