using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int NationTransferFailedText = 16700;
    private const int NationTransferDoneText = 16701;
    private const int NationTransferCertificate = 810096000;
    private const int NationTransferWarText = 16711;
    private static readonly Dictionary<int, int> NationTransferRefusalTexts = new()
    {
        [2] = 16702,
        [3] = 16703,
        [4] = 16704,
        [5] = 16705,
        [6] = 16706,
        [7] = 16710,
        [9] = 10750,
        [10] = 11303,
    };

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
    private bool _transferShown, _transferInFlight;

    private void NationTransferInit()
    {
        BuildNationTransferPanel();
        Net.I.NationTransferOpenEvent += OnNationTransferOpen;
        Net.I.NationTransferRefusedEvent += OnNationTransferRefused;
        Net.I.NationTransferDoneEvent += OnNationTransferDone;
        Net.I.NationTransferWarEvent += OnNationTransferWar;
    }

    private void NationTransferDispose()
    {
        Net.I.NationTransferOpenEvent -= OnNationTransferOpen;
        Net.I.NationTransferRefusedEvent -= OnNationTransferRefused;
        Net.I.NationTransferDoneEvent -= OnNationTransferDone;
        Net.I.NationTransferWarEvent -= OnNationTransferWar;
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
        foreach (var (_, button) in _transferRows)
            button.QueueFree();
        _transferRows.Clear();
        _transferPicks.Clear();
        _transferSelected = null;
        _transferInFlight = false;
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
        _transferSelected = candidate;
        foreach (var (row, button) in _transferRows)
            button.SetPressedNoSignal(row.Slot == candidate.Slot);
        var pick = _transferPicks[candidate.Slot];
        _transferEditor.Load(GenderChange.AllowedRaces(candidate.Class), pick.Race, pick.Face, pick.Hair);
        ShowTransferLook();
    }

    private void OnTransferLookChanged()
    {
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
        if (_transferInFlight || _transferPicks.Count == 0) return;
        Notice.Confirm(this,
            $"All {_transferPicks.Count} characters of your account move to {Nations.Name(_transferNation)} and your {ItemData.DisplayName(NationTransferCertificate).Trim()} is used. You return to the character screen afterwards.",
            "Transfer", "Cancel", SendNationTransfer, title: "Nation Transfer");
    }

    private void SendNationTransfer()
    {
        _transferInFlight = true;
        _transferConfirm.Disabled = true;
        SetTransferStatus("Transferring…", false);
        var picks = new List<NationTransferPick>(_transferPicks.Values);
        picks.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        Net.I.SendNationTransfer(picks);
    }

    private void CancelNationTransfer()
    {
        if (!_transferShown) return;
        _transferShown = false;
        _transferPanel.Visible = false;
        _transferPreview.Clear();
        if (!_transferInFlight) Net.I.SendNationTransferCancel();
    }

    private void OnNationTransferRefused(int result)
    {
        _transferInFlight = false;
        _transferConfirm.Disabled = false;
        string text = ItemData.Text(
            NationTransferRefusalTexts.TryGetValue(result, out int id) ? id : NationTransferFailedText,
            "Transfer failed");
        if (_transferShown) SetTransferStatus(text, true);
        else CombatNotice(text);
    }

    private void OnNationTransferWar(int karus, int elmorad)
    {
        _transferInFlight = false;
        _transferConfirm.Disabled = false;
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

    private void SetTransferStatus(string text, bool warn)
    {
        _transferStatus.Text = text;
        _transferStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }
}
