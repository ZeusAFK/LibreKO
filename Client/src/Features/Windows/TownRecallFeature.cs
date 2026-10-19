using Godot;

namespace LibreKO.Features.Windows;

internal sealed class TownRecallFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly IPlayerRequests playerRequests;
    private readonly SiegeTowerSystem siegeTower;

    public TownRecallFeature(IWorldContext context, SelfSession self, IPlayerRequests playerRequests, SiegeTowerSystem siegeTower)
    {
        this.context = context;
        this.self = self;
        this.playerRequests = playerRequests;
        this.siegeTower = siegeTower;
    }

    public void Dispose()
    {
    }

    private const int TextLowHp = 1903;

    private CanvasLayer layer = null!;
    private ConfirmationDialog dialog = null!;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Prompts };
        context.Root.AddChild(layer);

        dialog = new ConfirmationDialog { Title = "Town Recall" };
        dialog.DialogText = "Recall to town?\n(You must be above 50% HP.)";
        dialog.GetOkButton().Text = "Recall";
        dialog.GetCancelButton().Text = "Cancel";
        dialog.Confirmed += Press;
        layer.AddChild(dialog);
    }

    public void TryOpen()
    {
        if (!Ready()) return;
        dialog.PopupCentered();
    }

    internal void Press()
    {
        if (!Ready()) return;
        Confirm();
    }

    private bool Ready()
    {
        if (!context.Ready || context.Self.Dead || siegeTower.RefusedTownOnTower()) return false;
        if (!self.Vitals.BelowHalfHp) return true;
        context.Log.Notice(ItemData.Text(TextLowHp,
            "You cannot teleport back to town when you have half the HP or less"));
        return false;
    }

    private void Confirm()
    {
        playerRequests.GoTown();
        context.Log.Notice("Recalling to town…");
        Audio.Play(Sfx.WarpZone, context.Self.Body.GlobalPosition);
    }
}
