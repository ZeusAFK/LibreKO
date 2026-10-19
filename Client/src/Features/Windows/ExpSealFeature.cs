using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ExpSealFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ExpSealSession expSeal;
    private readonly SelfSession self;
    private readonly IExpSealRequests requests;
    private readonly ExpBarFeature? bar;

    public ExpSealFeature(IWorldContext context, ExpSealSession expSeal, SelfSession self, IExpSealRequests requests, ExpBarFeature? expBar)
    {
        this.context = context;
        this.expSeal = expSeal;
        this.self = self;
        this.requests = requests;
        bar = expBar;
    }

    private const string Title = "EXP Seal";
    private const int BodyWidth = 380;
    private const float JarSize = 44f;
    private const int BagStart = 14;
    private static readonly Color RefusalColour = new("ff3b3b");
    private static readonly Color GainColour = new(1f, 1f, 0f);

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    internal TextureRect JarRect { get; private set; } = null!;
    private Label percent = null!;
    private Label amountLabel = null!;
    private Button onButton = null!;
    private Button offButton = null!;
    internal OptionButton Pick { get; private set; } = null!;
    internal bool JarPlaced { get; set; }

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        BuildWindow();
        expSeal.ExpSealEvent += On;
    }

    public void Dispose() => expSeal.ExpSealEvent -= On;

    internal void BuildWindow()
    {
        Window = new HudWindow("exp_seal", Title, BodyWidth) { Visible = false };
        Window.Closed += Close;
        Layer.AddChild(Window);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        Window.Body.AddChild(body);

        JarRect = new TextureRect
        {
            CustomMinimumSize = new Vector2(JarSize, JarSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            TooltipText = ItemData.Text(ExpSeal.HelpText, "[Experience Seal System]"),
        };
        var jarFrame = UiTheme.Section();
        jarFrame.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        jarFrame.AddChild(JarRect);
        body.AddChild(jarFrame);

        percent = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        body.AddChild(percent);
        amountLabel = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        body.AddChild(amountLabel);

        var toggle = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        toggle.AddThemeConstantOverride("separation", 10);
        toggle.AddChild(UiTheme.Text(Title, 13, UiTheme.TextHi));
        onButton = UiTheme.SmallButton("ON", "");
        onButton.Pressed += () => requests.Toggle(true);
        offButton = UiTheme.SmallButton("OFF", "");
        offButton.Pressed += () => requests.Toggle(false);
        toggle.AddChild(onButton);
        toggle.AddChild(offButton);
        body.AddChild(toggle);

        var store = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        store.AddThemeConstantOverride("separation", 8);
        Pick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        Pick.AddItem(ItemData.Text(ExpSeal.ObtainText, "Obtain EXP"));
        foreach (var amount in ExpSeal.Amounts) Pick.AddItem(amount.ToString("N0"));
        store.AddChild(Pick);
        var start = UiTheme.ActionButton(ItemData.Text(ExpSeal.ObtainText, "Obtain EXP"), "");
        start.Pressed += StoreSealed;
        store.AddChild(start);
        body.AddChild(store);
        Render();
    }

    internal void Open(int jarSlot = -1)
    {
        JarPlaced = jarSlot >= 0;
        JarRect.Texture = JarPlaced ? ItemIcons.Get(ExpSeal.EmptyJar) : null;
        Render();
        UiKit.ShowOnTop(Window);
    }

    private void Close()
    {
        Window.Visible = false;
        JarPlaced = false;
        JarRect.Texture = null;
        Pick.Selected = 0;
    }

    private void Render()
    {
        long total = expSeal.SealedExperience;
        percent.Text = $"Sealed EXP: {(int)ExpSeal.Percent(total)} %";
        amountLabel.Text = $"( {total:N0} / {ExpSeal.Capacity:N0} )";
        onButton.Modulate = expSeal.ExpSealState == ExpSealState.On ? UiTheme.GoldBright : Colors.White;
        offButton.Modulate = expSeal.ExpSealState == ExpSealState.Off ? UiTheme.GoldBright : Colors.White;
    }

    private void StoreSealed()
    {
        long amount = Pick.Selected > 0 ? ExpSeal.Amounts[Pick.Selected - 1] : 0;
        if (ExpSeal.LocalRefusal(JarPlaced, expSeal.SealedExperience, amount) is { } refusal)
        {
            context.Log.Notice(ItemData.Text(refusal, ""), RefusalColour);
            return;
        }
        requests.Store(FirstBagPos(ExpSeal.EmptyJar), amount);
    }

    private byte FirstBagPos(int itemId)
    {
        for (int slot = BagStart; slot < self.Inventory.Length; slot++)
            if (self.Inventory[slot].ItemId == itemId) return (byte)(slot - BagStart);
        return ExpSeal.NoBagPos;
    }

    internal void UseJar(NpcDialogStyle flag)
    {
        int jar = ExpSeal.JarForFlag(flag);
        if (jar != 0) requests.Use(jar, FirstBagPos(jar));
    }

    private void On(ExpSealReply reply)
    {
        switch (reply.Sub)
        {
            case ExpSealSubOpcode.Store:
                if (reply.Code == ExpSealResult.Succeeded)
                {
                    JarPlaced = false;
                    JarRect.Texture = null;
                }
                else if (ExpSeal.StoreRefusal(reply.Code) is { } refusal)
                    context.Log.Notice(ItemData.Text(refusal, ""), RefusalColour);
                break;
            case ExpSealSubOpcode.Total when expSeal.SealedGain > 0:
                context.Log.Notice(ItemData.Text(ExpSeal.GainText, "%I64d experience sealed in the pot.")
                    .Replace("%I64d", expSeal.SealedGain.ToString()), GainColour);
                break;
            case ExpSealSubOpcode.UseJar when ExpSeal.UseRefusal(reply.Code) is { } use:
                string text = ItemData.Text(use.Id, "");
                context.Log.Notice(use.WithCode ? $"{text}({(short)reply.Code})" : text, RefusalColour);
                break;
        }
        Render();
        bar?.Update();
    }
}
