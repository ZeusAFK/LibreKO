using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int NationTransferDoneText = 16701;
    private const int NationTransferCertificate = 810096000;
    private const int NationTransferWarText = 16711;

    private CanvasLayer _transferLayer = null!;
    private HudWindow _transferPanel = null!;
    private LookPreview _transferPreview = null!;
    private LookEditor _transferEditor = null!;
    private VBoxContainer _transferList = null!;
    private Label _transferHeader = null!, _transferStatus = null!;
    private Button _transferConfirm = null!;
    private readonly List<(NationTransferCandidate Candidate, Button Button)> _transferRows = new();
    private readonly Dictionary<int, NationTransferPick> _transferPicks = new();
    private NationTransferCandidate? _transferSelected;
    private int _transferNation;
    private bool _transferShown, _transferInFlight, _transferCompleted;
    private int _transferRevision;
    private Notice? _transferNotice;

    private void NationTransferInit()
    {
        BuildNationTransferPanel();
        Net.I.NationTransferOpenEvent += OnNationTransferOpen;
        Net.I.NationTransferRefusedEvent += OnNationTransferRefused;
        Net.I.NationTransferDoneEvent += OnNationTransferDone;
        Net.I.NationTransferWarEvent += OnNationTransferWar;
        Net.I.NationTransferResetEvent += OnNationTransferReset;
    }

    private void NationTransferDispose()
    {
        Net.I.NationTransferOpenEvent -= OnNationTransferOpen;
        Net.I.NationTransferRefusedEvent -= OnNationTransferRefused;
        Net.I.NationTransferDoneEvent -= OnNationTransferDone;
        Net.I.NationTransferWarEvent -= OnNationTransferWar;
        Net.I.NationTransferResetEvent -= OnNationTransferReset;
    }

    private void BuildNationTransferPanel()
    {
        _transferLayer = new CanvasLayer { Layer = 74 };
        AddChild(_transferLayer);

        _transferPanel = new HudWindow("nationtransfer", "Nation Transfer") { Visible = false };
        _transferPanel.Closed += CancelNationTransfer;
        _transferLayer.AddChild(_transferPanel);

        _transferHeader = HudStyle.Label(13);
        _transferHeader.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _transferPanel.Body.AddChild(_transferHeader);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _transferPanel.Body.AddChild(row);

        var listColumn = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        listColumn.AddThemeConstantOverride("separation", 4);
        listColumn.AddChild(UiTheme.SectionTitle("Characters"));
        _transferList = new VBoxContainer();
        _transferList.AddThemeConstantOverride("separation", 4);
        listColumn.AddChild(_transferList);
        row.AddChild(listColumn);

        _transferPreview = new LookPreview(LookPreviewWidth, LookPreviewHeight);
        row.AddChild(LookPreviewColumn(_transferPreview));

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        row.AddChild(form);
        _transferEditor = new LookEditor();
        _transferEditor.Changed += OnTransferLookChanged;
        form.AddChild(_transferEditor);

        _transferStatus = HudStyle.Label(12);
        _transferStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _transferStatus.CustomMinimumSize = new Vector2(230, 0);
        form.AddChild(_transferStatus);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        form.AddChild(actions);
        _transferConfirm = LookActionButton("Transfer", OnTransferPressed);
        actions.AddChild(_transferConfirm);
        actions.AddChild(LookActionButton("Cancel", CancelNationTransfer));
    }

    private void OnNationTransferOpen(IReadOnlyList<NationTransferCandidate> candidates)
    {
        CloseNpcDialog();
        OpenNationTransfer(candidates);
    }

    private void OpenNationTransfer(IReadOnlyList<NationTransferCandidate> candidates)
    {
        if (_transferInFlight || _transferNotice != null || _transferCompleted) return;
        _transferRevision++;
        foreach (var (_, button) in _transferRows)
            button.QueueFree();
        _transferRows.Clear();
        _transferPicks.Clear();
        _transferSelected = null;
        if (candidates.Count == 0) return;

        _transferNation = candidates[0].Nation;
        _transferHeader.Text = $"Every character of your account moves to {Nations.Name(_transferNation)}. Choose how each one will look.";
        foreach (var candidate in candidates)
        {
            _transferPicks[candidate.Slot] = new NationTransferPick(candidate.Slot, candidate.Name, candidate.Race, candidate.Face, candidate.Hair);
            var button = UiTheme.TopTabButton($"{candidate.Name}  ·  {CharacterClassCatalog.DisplayName(candidate.Class)}", 13);
            var picked = candidate;
            button.Pressed += () => SelectTransferCharacter(picked);
            _transferList.AddChild(button);
            _transferRows.Add((candidate, button));
        }
        SelectTransferCharacter(candidates[0]);
        SetTransferStatus("", false);
        _transferConfirm.Disabled = false;
        _transferPanel.Visible = true;
        _transferShown = true;
    }

    private void SelectTransferCharacter(NationTransferCandidate candidate)
    {
        if (_transferInFlight || _transferNotice != null) return;
        _transferSelected = candidate;
        foreach (var (row, button) in _transferRows)
            button.SetPressedNoSignal(row.Slot == candidate.Slot);
        var pick = _transferPicks[candidate.Slot];
        _transferEditor.Load(GenderChange.AllowedRaces(candidate.Class), pick.Race, pick.Face, pick.Hair);
        ShowTransferLook();
    }

    private void OnTransferLookChanged()
    {
        if (_transferInFlight || _transferNotice != null) return;
        if (_transferSelected is not { } selected) return;
        _transferPicks[selected.Slot] = new NationTransferPick(selected.Slot, selected.Name,
            _transferEditor.Race, _transferEditor.Face, _transferEditor.Hair);
        ShowTransferLook();
    }

    private void ShowTransferLook()
    {
        _transferEditor.Refresh();
        _transferPreview.Show(_transferEditor.Race, _transferEditor.Face, _transferEditor.HairStyle, _transferEditor.HairColour);
    }

    private void OnTransferPressed()
    {
        if (!_transferShown || _transferInFlight || _transferNotice != null || _transferPicks.Count == 0) return;
        int revision = ++_transferRevision;
        var picks = _transferPicks.Values.OrderBy(pick => pick.Slot).ToArray();
        SetTransferLocked(true);
        _transferNotice = Notice.Confirm(this,
            $"All {picks.Length} characters of your account move to {Nations.Name(_transferNation)} and your {ItemData.DisplayName(NationTransferCertificate).Trim()} is used. You return to the character screen afterwards.",
            "Transfer", "Cancel", () => SendNationTransfer(revision, picks),
            () => CancelTransferConfirmation(revision), title: "Nation Transfer");
    }

    private void SendNationTransfer(int revision, IReadOnlyList<NationTransferPick> picks)
    {
        if (revision != _transferRevision || !_transferShown || _transferInFlight || _transferNotice == null) return;
        _transferNotice = null;
        _transferInFlight = true;
        SetTransferStatus("Transferring…", false);
        if (!Net.I.SendNationTransfer(picks)) OnNationTransferRefused(NationTransferWire.FailedText);
    }

    private void CancelTransferConfirmation(int revision)
    {
        if (revision != _transferRevision || _transferInFlight || _transferNotice == null) return;
        _transferNotice = null;
        SetTransferLocked(false);
    }

    private void DismissTransferConfirmation()
    {
        _transferRevision++;
        if (_transferNotice is { } notice && GodotObject.IsInstanceValid(notice)) notice.Close();
        _transferNotice = null;
    }

    private void SetTransferLocked(bool locked)
    {
        foreach (var (_, button) in _transferRows) button.Disabled = locked;
        _transferConfirm.Disabled = locked || _transferPicks.Count == 0;
        if (!locked && _transferShown && _transferSelected is { } selected) SelectTransferCharacter(selected);
    }

    private void CancelNationTransfer()
    {
        if (!_transferShown) return;
        _transferShown = false;
        _transferPanel.Visible = false;
        DismissTransferConfirmation();
        _transferPreview.Clear();
        if (!_transferInFlight) Net.I.SendNationTransferCancel();
    }

    private void OnNationTransferRefused(int textId)
    {
        DismissTransferConfirmation();
        _transferInFlight = false;
        SetTransferLocked(false);
        if (!_transferShown) Net.I.SendNationTransferCancel();
        string text = ItemData.Text(textId, "Transfer failed");
        if (_transferShown) SetTransferStatus(text, true);
        else CombatNotice(text);
    }

    private void OnNationTransferWar(int karus, int elmorad)
    {
        DismissTransferConfirmation();
        _transferInFlight = false;
        SetTransferLocked(false);
        if (!_transferShown) Net.I.SendNationTransferCancel();
        var text = new System.Text.StringBuilder(ItemData.Text(NationTransferWarText,
            "Currently nation transfer is not available. current war status Karus %d : El Morad %d"));
        foreach (int score in new[] { karus, elmorad })
        {
            int at = text.ToString().IndexOf("%d", System.StringComparison.Ordinal);
            if (at >= 0) text.Remove(at, 2).Insert(at, score.ToString());
        }
        if (_transferShown) SetTransferStatus(text.ToString(), true);
        else Notice.Show(this, text.ToString(), "Nation Transfer");
    }

    private void OnNationTransferDone()
    {
        if (!_transferInFlight || _transferCompleted) return;
        _transferInFlight = false;
        _transferCompleted = true;
        DismissTransferConfirmation();
        _transferShown = false;
        _transferPanel.Visible = false;
        _transferPreview.Clear();
        Notice.Show(this, ItemData.Text(NationTransferDoneText, "Character transfer was successful. Please connect again"),
            "Nation Transfer", () =>
            {
                Net.I.ReturnToCharSelect();
                GetTree().ChangeSceneToFile("res://scenes/CharSelect.tscn");
            });
    }

    private void OnNationTransferReset()
    {
        DismissTransferConfirmation();
        _transferInFlight = false;
        _transferShown = false;
        _transferPanel.Visible = false;
        _transferPreview.Clear();
        SetTransferLocked(false);
    }

    private void SetTransferStatus(string text, bool warn)
    {
        _transferStatus.Text = text;
        _transferStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }
}
