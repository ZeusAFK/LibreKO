using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _townRecallLayer = null!;
    private ConfirmationDialog _townRecallDialog = null!;

    private void TownRecallInit()
    {
        _townRecallLayer = new CanvasLayer { Layer = 76 };
        AddChild(_townRecallLayer);

        _townRecallDialog = new ConfirmationDialog { Title = "Town Recall" };
        _townRecallDialog.DialogText = "Recall to town?\n(You must be above 50% HP.)";
        _townRecallDialog.GetOkButton().Text = "Recall";
        _townRecallDialog.GetCancelButton().Text = "Cancel";
        _townRecallDialog.Confirmed += TownRecallConfirm;
        _townRecallLayer.AddChild(_townRecallDialog);
    }

    public void TownRecallTryOpen()
    {
        if (!TownRecallReady()) return;
        _townRecallDialog.PopupCentered();
    }

    private void TownRecallPress()
    {
        if (!TownRecallReady()) return;
        TownRecallConfirm();
    }

    private bool TownRecallReady()
    {
        if (!_worldReady || _selfDead) return false;
        if (!Vitals.BelowHalfHp) return true;
        Chat.Info("Town recall failed — heal above 50% HP first.");
        return false;
    }

    private void TownRecallConfirm()
    {
        Net.I.SendTownRecall();
        Chat.Info("Recalling to town…");
        Audio.Play(Sfx.WarpZone, _self.GlobalPosition);
    }
}
