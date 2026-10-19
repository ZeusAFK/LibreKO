using Godot;

namespace LibreKO.Features.Windows;

internal sealed class RouletteFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly RouletteSession roulette;
    private readonly IRouletteRequests requests;

    public RouletteFeature(IWorldContext context, RouletteSession roulette, IRouletteRequests requests)
    {
        this.context = context;
        this.roulette = roulette;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label coinsLabel = null!;
    private Label resultLabel = null!;
    private Button spinBtn = null!;
    private bool shown;

    public bool IsOpen => shown;

    private int coins;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(layer);
        panel = new HudWindow("roulette", "Event Roulette") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle("Event Roulette"));

        coinsLabel = UiTheme.Text("Event Coins: -", 14, UiTheme.Gold);
        root.AddChild(coinsLabel);

        spinBtn = new Button { Text = "Spin (1 coin)", FocusMode = Control.FocusModeEnum.None };
        spinBtn.Pressed += OnSpinPressed;
        root.AddChild(spinBtn);

        resultLabel = UiTheme.Text("Spin to win a prize!", 13, UiTheme.TextLo);
        root.AddChild(resultLabel);

        roulette.RouletteStatusEvent += OnStatus;
        roulette.RouletteSpinEvent += OnSpin;
    }

    public void Dispose()
    {
        roulette.RouletteStatusEvent -= OnStatus;
        roulette.RouletteSpinEvent -= OnSpin;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        panel.Visible = true;
        shown = true;
        requests.Status();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void OnSpinPressed()
    {
        if (coins < 1)
        {
            resultLabel.Text = "Not enough event coins.";
            return;
        }
        requests.Spin();
    }

    private void OnStatus(int coins)
    {
        this.coins = coins;
        coinsLabel.Text = $"Event Coins: {coins}";
        spinBtn.Disabled = coins < 1;
    }

    private void OnSpin(bool ok, int prizeItemId, int prizeGold)
    {
        if (!ok)
        {
            resultLabel.Text = "Not enough event coins.";
            requests.Status();
            return;
        }

        string prize = prizeGold > 0
            ? $"{prizeGold:N0} gold"
            : (prizeItemId > 0 ? $"item #{prizeItemId}" : "nothing");
        resultLabel.Text = $"You won {prize}!";

        requests.Status();
    }
}
