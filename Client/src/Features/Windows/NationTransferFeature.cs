using Godot;

namespace LibreKO.Features.Windows;

internal sealed class NationTransferFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly NationTransferSession nationTransfer;
    private readonly INationTransferRequests requests;

    public NationTransferFeature(IWorldContext context, NationTransferSession nationTransfer, INationTransferRequests requests)
    {
        this.context = context;
        this.nationTransfer = nationTransfer;
        this.requests = requests;
    }

    private const int DoneText = 16701;
    private const int Certificate = 810096000;
    private const int WarText = 16711;

    private CanvasLayer transferLayer = null!;
    internal HudWindow TransferPanel { get; private set; } = null!;
    private LookPreview transferPreview = null!;
    private LookEditor transferEditor = null!;
    private VBoxContainer transferList = null!;
    private Label transferHeader = null!;
    private Label transferStatus = null!;
    private Button transferConfirm = null!;
    private readonly List<(NationTransferCandidate Candidate, Button Button)> transferRows = new();

    private readonly Dictionary<int, NationTransferPick> transferPicks = new();

    private NationTransferCandidate? transferSelected;
    private int transfer;
    private bool transferShown;
    private bool transferInFlight;
    private bool transferCompleted;
    private int transferRevision;
    private Notice? transferNotice;

    public void Build()
    {
        BuildPanel();
        nationTransfer.NationTransferOpenEvent += OnOpen;
        nationTransfer.NationTransferRefusedEvent += OnRefused;
        nationTransfer.NationTransferDoneEvent += OnDone;
        nationTransfer.NationTransferWarEvent += OnWar;
        nationTransfer.NationTransferResetEvent += OnReset;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (transferShown) Cancel();
        else return false;
        return true;
    }

    public void Dispose()
    {
        nationTransfer.NationTransferOpenEvent -= OnOpen;
        nationTransfer.NationTransferRefusedEvent -= OnRefused;
        nationTransfer.NationTransferDoneEvent -= OnDone;
        nationTransfer.NationTransferWarEvent -= OnWar;
        nationTransfer.NationTransferResetEvent -= OnReset;
    }

    internal void BuildPanel()
    {
        transferLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(transferLayer);

        TransferPanel = new HudWindow("nationtransfer", "Nation Transfer") { Visible = false };
        TransferPanel.Closed += Cancel;
        transferLayer.AddChild(TransferPanel);

        transferHeader = HudStyle.Label(13);
        transferHeader.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        TransferPanel.Body.AddChild(transferHeader);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        TransferPanel.Body.AddChild(row);

        var listColumn = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        listColumn.AddThemeConstantOverride("separation", 4);
        listColumn.AddChild(UiTheme.SectionTitle("Characters"));
        transferList = new VBoxContainer();
        transferList.AddThemeConstantOverride("separation", 4);
        listColumn.AddChild(transferList);
        row.AddChild(listColumn);

        transferPreview = new LookPreview(GenderChangeFeature.LookPreviewWidth, GenderChangeFeature.LookPreviewHeight);
        row.AddChild(GenderChangeFeature.LookPreviewColumn(transferPreview));

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        row.AddChild(form);
        transferEditor = new LookEditor();
        transferEditor.Changed += OnTransferLookChanged;
        form.AddChild(transferEditor);

        transferStatus = HudStyle.Label(12);
        transferStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        transferStatus.CustomMinimumSize = new Vector2(230, 0);
        form.AddChild(transferStatus);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        form.AddChild(actions);
        transferConfirm = GenderChangeFeature.LookActionButton("Transfer", OnTransferPressed);
        actions.AddChild(transferConfirm);
        actions.AddChild(GenderChangeFeature.LookActionButton("Cancel", Cancel));
    }

    private void OnOpen(IReadOnlyList<NationTransferCandidate> candidates)
    {
        context.NpcDialog.Close();
        Open(candidates);
    }

    internal void Open(IReadOnlyList<NationTransferCandidate> candidates)
    {
        if (transferInFlight || transferNotice != null || transferCompleted) return;
        transferRevision++;
        foreach (var (_, button) in transferRows)
            button.QueueFree();
        transferRows.Clear();
        transferPicks.Clear();
        transferSelected = null;
        if (candidates.Count == 0) return;

        transfer = candidates[0].Nation;
        transferHeader.Text = $"Every character of your account moves to {Nations.Name(transfer)}. Choose how each one will look.";
        foreach (var candidate in candidates)
        {
            transferPicks[candidate.Slot] = new NationTransferPick(candidate.Slot, candidate.Name, candidate.Race, candidate.Face, candidate.Hair);
            var button = UiTheme.TopTabButton($"{candidate.Name}  ·  {CharacterClassCatalog.DisplayName(candidate.Class)}", 13);
            var picked = candidate;
            button.Pressed += () => SelectTransferCharacter(picked);
            transferList.AddChild(button);
            transferRows.Add((candidate, button));
        }
        SelectTransferCharacter(candidates[0]);
        SetTransferStatus("", false);
        transferConfirm.Disabled = false;
        TransferPanel.Visible = true;
        transferShown = true;
    }

    private void SelectTransferCharacter(NationTransferCandidate candidate)
    {
        if (transferInFlight || transferNotice != null) return;
        transferSelected = candidate;
        foreach (var (row, button) in transferRows)
            button.SetPressedNoSignal(row.Slot == candidate.Slot);
        var pick = transferPicks[candidate.Slot];
        transferEditor.Load(GenderChange.AllowedRaces(candidate.Class), pick.Race, pick.Face, pick.Hair);
        ShowTransferLook();
    }

    private void OnTransferLookChanged()
    {
        if (transferInFlight || transferNotice != null) return;
        if (transferSelected is not { } selected) return;
        transferPicks[selected.Slot] = new NationTransferPick(selected.Slot, selected.Name,
            transferEditor.Race, transferEditor.Face, transferEditor.Hair);
        ShowTransferLook();
    }

    private void ShowTransferLook()
    {
        transferEditor.Refresh();
        transferPreview.Show(transferEditor.Race, transferEditor.Face, transferEditor.HairStyle, transferEditor.HairColour);
    }

    private void OnTransferPressed()
    {
        if (!transferShown || transferInFlight || transferNotice != null || transferPicks.Count == 0) return;
        int revision = ++transferRevision;
        var picks = transferPicks.Values.OrderBy(pick => pick.Slot).ToArray();
        SetTransferLocked(true);
        transferNotice = Notice.Confirm(context.Root,
            $"All {picks.Length} characters of your account move to {Nations.Name(transfer)} and your {ItemData.DisplayName(Certificate).Trim()} is used. You return to the character screen afterwards.",
            "Transfer", "Cancel", () => Send(revision, picks),
            () => CancelTransferConfirmation(revision), title: "Nation Transfer");
    }

    private void Send(int revision, IReadOnlyList<NationTransferPick> picks)
    {
        if (revision != transferRevision || !transferShown || transferInFlight || transferNotice == null) return;
        transferNotice = null;
        transferInFlight = true;
        SetTransferStatus("Transferring…", false);
        if (!requests.Submit(picks)) OnRefused(NationTransferWire.FailedText);
    }

    private void CancelTransferConfirmation(int revision)
    {
        if (revision != transferRevision || transferInFlight || transferNotice == null) return;
        transferNotice = null;
        SetTransferLocked(false);
    }

    private void DismissTransferConfirmation()
    {
        transferRevision++;
        if (transferNotice is { } notice && GodotObject.IsInstanceValid(notice)) notice.Close();
        transferNotice = null;
    }

    private void SetTransferLocked(bool locked)
    {
        foreach (var (_, button) in transferRows) button.Disabled = locked;
        transferConfirm.Disabled = locked || transferPicks.Count == 0;
        if (!locked && transferShown && transferSelected is { } selected) SelectTransferCharacter(selected);
    }

    internal void Cancel()
    {
        if (!transferShown) return;
        transferShown = false;
        TransferPanel.Visible = false;
        DismissTransferConfirmation();
        transferPreview.Clear();
        if (!transferInFlight) requests.Cancel();
    }

    private void OnRefused(int textId)
    {
        DismissTransferConfirmation();
        transferInFlight = false;
        SetTransferLocked(false);
        if (!transferShown) requests.Cancel();
        string text = ItemData.Text(textId, "Transfer failed");
        if (transferShown) SetTransferStatus(text, true);
        else context.Log.Notice(text);
    }

    private void OnWar(int karus, int elmorad)
    {
        DismissTransferConfirmation();
        transferInFlight = false;
        SetTransferLocked(false);
        if (!transferShown) requests.Cancel();
        var text = new System.Text.StringBuilder(ItemData.Text(WarText,
            "Currently nation transfer is not available. current war status Karus %d : El Morad %d"));
        foreach (int score in new[] { karus, elmorad })
        {
            int at = text.ToString().IndexOf("%d", System.StringComparison.Ordinal);
            if (at >= 0) text.Remove(at, 2).Insert(at, score.ToString());
        }
        if (transferShown) SetTransferStatus(text.ToString(), true);
        else Notice.Show(context.Root, text.ToString(), "Nation Transfer");
    }

    private void OnDone()
    {
        if (!transferInFlight || transferCompleted) return;
        transferInFlight = false;
        transferCompleted = true;
        DismissTransferConfirmation();
        transferShown = false;
        TransferPanel.Visible = false;
        transferPreview.Clear();
        Notice.Show(context.Root, ItemData.Text(DoneText, "Character transfer was successful. Please connect again"),
            "Nation Transfer", () =>
            {
                Game.Connection.ReturnToCharSelect();
                context.Root.GetTree().ChangeSceneToFile("res://scenes/CharSelect.tscn");
            });
    }

    private void OnReset()
    {
        DismissTransferConfirmation();
        transferInFlight = false;
        transferShown = false;
        TransferPanel.Visible = false;
        transferPreview.Clear();
        SetTransferLocked(false);
    }

    private void SetTransferStatus(string text, bool warn)
    {
        transferStatus.Text = text;
        transferStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }
}
