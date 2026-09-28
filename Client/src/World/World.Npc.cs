using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _npcLayer = null!;
    private HudWindow _npcPanel = null!;
    private RichTextLabel _npcBody = null!;
    private ScrollContainer _npcMenuScroll = null!;
    private VBoxContainer _npcMenuBox = null!;
    private VBoxContainer _npcQuestContent = null!;
    private ScrollContainer _npcQuestScroll = null!;
    private bool _npcDialogShown;
    private bool _npcBodyEmpty;
    private string _npcDialogScript = "";

    private CanvasLayer _balloonLayer = null!;
    private PanelContainer _balloonPanel = null!;
    private Label _balloonLabel = null!;
    private int _balloonToken;

    private const float TalkPickRadius = 70f;
    private const int OffersPerPage = 10;
    private const int MenuRowsVisible = OffersPerPage + 1;
    private const float MenuRowHeight = 39f;

    private System.Collections.Generic.List<QuestData.Offer> _npcOffers = new();
    private int _npcOfferPage;
    private string _npcOfferHeader = "", _npcOfferTitle = "";
    private const float NpcInteractRange = Net.NpcInteractRange;
    private const double NpcRangeInterval = 0.25;

    private int _npcTalkId = -1;
    private double _npcRangeAccum;

    private void NpcInit()
    {
        BuildNpcDialog();
        BuildNpcBalloon();
        BuildInteractionDialogs();

        Net.I.NpcDialogEvent += OnNpcDialog;
        Net.I.NpcSayEvent += OnNpcSay;
        Net.I.NpcMsgEvent += OnNpcMsg;
        Net.I.NpcWindowEvent += OnNpcWindow;
    }

    private void NpcDispose()
    {
        Net.I.NpcDialogEvent -= OnNpcDialog;
        Net.I.NpcSayEvent -= OnNpcSay;
        Net.I.NpcMsgEvent -= OnNpcMsg;
        Net.I.NpcWindowEvent -= OnNpcWindow;
    }

    private bool TryInteractAt(Vector2 mouse)
    {
        if (TryOpenStallAt(mouse)) return true;
        if (TryOpenPlayerMenu(mouse)) return true;

        var best = PickEntityAt(mouse, TalkPickRadius, out int bestId, out _);
        if (best == null) return TryOpenWarpGate(mouse) || TryOpenAnvil(mouse);
        if (best.Attackable) return false;

        Select(bestId, best);
        if (!best.Attackable) TalkToNpc(bestId);
        return true;
    }

    private bool HasNearbyNpc()
    {
        if (!_worldReady || _self == null || _selfDead) return false;
        foreach (var (_, e) in _ents)
        {
            if (!e.IsNpc || e.Dead || e.Attackable) continue;
            if (FlatDistance(_self.Position, e.Body.Position) <= NpcInteractRange)
                return true;
        }
        return false;
    }

    public bool TalkToNearestNpc(string nameFragment)
    {
        if (!_worldReady || _self == null) return false;
        int bestId = -1;
        float bestDist = float.MaxValue;
        foreach (var (id, e) in _ents)
        {
            if (!e.IsNpc || e.Dead || e.Attackable) continue;
            if (nameFragment.Length > 0
                && !e.Name.Contains(nameFragment, System.StringComparison.OrdinalIgnoreCase)) continue;
            float d = FlatDistance(_self.Position, e.Body.Position);
            if (d > NpcInteractRange) continue;
            if (d < bestDist) { bestDist = d; bestId = id; }
        }
        if (bestId < 0) return false;
        TalkToNpc(bestId);
        return true;
    }

    private void TalkToNpc(int npcUniqueId)
    {
        if (_selfDead) return;
        if (!_ents.TryGetValue(npcUniqueId, out var e) || !e.IsNpc || e.Dead || e.Attackable) return;

        if (FlatDistance(_self.Position, e.Body.Position) > NpcInteractRange) return;

        StopForNpcTalk(e);

        _npcTalkId = npcUniqueId;
        _npcRangeAccum = 0;
        _vendorNpcId = npcUniqueId;
        _vendorNpcName = e.Name.Length > 0
            ? e.Name
            : GameData.I != null ? GameData.I.NpcName(e.NpcId, false) : "Merchant";

        Net.I.SendNpcEvent(npcUniqueId);
        Net.I.SendWarpListRequest(e.NpcId);
    }

    private void StopForNpcTalk(Ent e)
    {
        StopForInteraction();

        Vector3 toNpc = e.Body.Position - _self.Position;
        toNpc.Y = 0;
        if (toNpc.LengthSquared() <= 0.0004f) return;
        _faceDir = toNpc.Normalized();
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
        => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private void NpcTick(double delta)
    {
        if (_npcTalkId < 0) return;

        if (!AnyInteractionDialogShown()) { _npcTalkId = -1; return; }

        _npcRangeAccum += delta;
        if (_npcRangeAccum < NpcRangeInterval) return;
        _npcRangeAccum = 0;

        bool inRange = _ents.TryGetValue(_npcTalkId, out var e)
                       && !e.Dead
                       && FlatDistance(_self.Position, e.Body.Position) <= NpcInteractRange;
        if (inRange) return;

        CloseInteractionDialogs();
        _npcTalkId = -1;
    }

    private void BuildNpcDialog()
    {
        _npcLayer = new CanvasLayer { Layer = 73 };
        AddChild(_npcLayer);

        _npcPanel = new HudWindow("npc_dialog", "NPC", new Vector2(430, 160), 420) { Visible = false };
        _npcPanel.Closed += CloseNpcDialog;
        _npcLayer.AddChild(_npcPanel);

        var root = _npcPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var bodyPanel = UiTheme.Section();
        root.AddChild(bodyPanel);

        _npcBody = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420, 0),
        };
        _npcBody.AddThemeFontSizeOverride("normal_font_size", 14);
        _npcBody.AddThemeColorOverride("default_color", UiTheme.TextLo);
        bodyPanel.AddChild(_npcBody);

        _npcQuestScroll = new ScrollContainer
        {
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(_npcQuestScroll);
        _npcQuestContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _npcQuestContent.AddThemeConstantOverride("separation", 10);
        _npcQuestScroll.AddChild(_npcQuestContent);

        _npcMenuScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(_npcMenuScroll);
        _npcMenuBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _npcMenuBox.AddThemeConstantOverride("separation", 5);
        _npcMenuScroll.AddChild(_npcMenuBox);
    }

    private void OnNpcDialog(NpcDialog dlg)
    {
        _npcDialogScript = dlg.ScriptFile;
        if (dlg.HeaderText is { Length: > 0 } body)
            BeginNpcDialog(
                GameData.I != null ? GameData.I.NpcName(dlg.NpcId, false) : $"NPC #{dlg.NpcId}",
                body);
        else
            BeginNpcDialog(dlg.NpcId, dlg.HeaderTextId);

        int shown = 0;
        foreach (var (index, textId, text) in dlg.Buttons)
        {
            string label = text is { Length: > 0 } ? text : QuestText.Menu(textId);
            if (label.Length == 0) label = $"…({textId})";
            int menuIndex = index;
            AddNpcMenuButton($"{++shown}.   {label}", () => OnNpcMenuClick(menuIndex));
        }

        if (shown == 0 && !_npcBodyEmpty)
            AddNpcMenuButton($"{++shown}.   Close", CloseNpcDialog);
        EndNpcDialog(shown);
    }

    // Sub 7's text is the HEADER of the NPC's quest menu; retail builds the list client-side.
    private void OnNpcMsg(int textId, int npcId)
    {
        _npcOffers = QuestData.OffersAtNpc(
            npcId, QuestStateOf, Sheet.Level, _selfClass, Net.I.LastEnter.Nation, _zone);
        _npcOfferPage = 0;
        _npcOfferHeader = QuestText.Talk(textId, Net.I.LastEnter.Name ?? "");
        _npcOfferTitle = GameData.I != null ? GameData.I.NpcName(npcId, false) : $"NPC #{npcId}";
        ShowNpcOfferPage();
    }

    private void ShowNpcOfferPage()
    {
        BeginNpcDialog(_npcOfferTitle, _npcOfferHeader);
        if (_npcOffers.Count == 0)
        {
            if (_npcBodyEmpty) { CloseNpcDialog(); return; }
            AddNpcMenuButton("Close", CloseNpcDialog);
            EndNpcDialog(1);
            return;
        }

        int pages = (_npcOffers.Count + OffersPerPage - 1) / OffersPerPage;
        _npcOfferPage = ((_npcOfferPage % pages) + pages) % pages;
        int first = _npcOfferPage * OffersPerPage;
        int shown = 0;
        for (int i = first; i < _npcOffers.Count && i < first + OffersPerPage; i++)
        {
            var offer = _npcOffers[i];
            string name = QuestData.Name(offer.QuestId, Net.I.LastEnter.Name ?? "");
            string tag = offer.State switch
            {
                QuestStateActive => "[In progress] ",
                QuestStateReadyToTurnIn => "[Ready] ",
                _ => "",
            };
            AddNpcMenuButton($"{++shown}.   {tag}{name}", () => AcceptNpcOffer(offer));
        }
        if (pages > 1)
            AddNpcMenuButton($"{++shown}.   Next page  ({_npcOfferPage + 1}/{pages})",
                () => { _npcOfferPage++; ShowNpcOfferPage(); });

        EndNpcDialog(shown);
    }

    private void AcceptNpcOffer(QuestData.Offer offer)
    {
        CloseNpcDialog();
        Net.I.SendQuestAccept(offer.QuestId);
    }

    private void BeginNpcDialog(int npcId, int textId) =>
        BeginNpcDialog(
            GameData.I != null ? GameData.I.NpcName(npcId, false) : $"NPC #{npcId}",
            QuestText.Talk(textId, Net.I.LastEnter.Name ?? ""));

    private void BeginNpcDialog(string title, string body)
    {
        _npcQuestScroll.Visible = false;
        _npcBody.GetParent<Control>().Visible = body.Length > 0;
        _npcPanel.Title = QuestMarkup.Plain(title, Net.I?.LastEnter.Name ?? "");
        _npcBody.Visible = body.Length > 0;
        _npcBody.Text = QuestMarkup.Rich(body, Net.I?.LastEnter.Name ?? "");
        _npcBodyEmpty = body.Length == 0;

        foreach (var c in _npcMenuBox.GetChildren()) { _npcMenuBox.RemoveChild(c); c.QueueFree(); }
    }

    private void ShowNpcServiceChoice(string body, params (string Label, System.Action Act)[] options)
    {
        BeginNpcDialog(_vendorNpcName, body);
        int shown = 0;
        foreach (var (label, act) in options)
        {
            var run = act;
            AddNpcMenuButton($"{++shown}.   {label}", () => { CloseNpcDialog(); run(); });
        }
        AddNpcMenuButton($"{++shown}.   Close", CloseNpcDialog);
        EndNpcDialog(shown);
    }

    private void AddNpcMenuButton(string label, System.Action onPressed)
    {
        var b = new Button
        {
            Text = QuestMarkup.Plain(label, Net.I?.LastEnter.Name ?? ""),
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 34),
        };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.Pressed += () => onPressed();
        _npcMenuBox.AddChild(b);
    }

    private void EndNpcDialog(int shown)
    {
        _npcMenuScroll.Visible = shown > 0;
        _npcMenuScroll.CustomMinimumSize = new Vector2(0,
            Mathf.Min(shown, MenuRowsVisible) * MenuRowHeight
            + (shown > MenuRowsVisible ? MenuRowHeight * 0.45f : 0f));

        if (_npcBodyEmpty && shown == 0) { CloseNpcDialog(); return; }
        ShowNpcDialog();
    }

    private void OnNpcMenuClick(int menuIndex)
    {
        Net.I.SendSelectMsg(menuIndex, _npcDialogScript, -1);
        CloseNpcDialog();
    }

    private void ShowNpcDialog()
    {
        _npcPanel.Visible = true;
        _npcDialogShown = true;
    }

    private void CloseNpcDialog()
    {
        if (!_npcDialogShown) return;
        _npcDialogShown = false;
        _npcPanel.Visible = false;
        HideItemTooltip();
    }

    private void BuildNpcBalloon()
    {
        _balloonLayer = new CanvasLayer { Layer = 68, Visible = false };
        AddChild(_balloonLayer);

        _balloonPanel = new PanelContainer();
        _balloonPanel.AddThemeStyleboxOverride("panel", UiTheme.Panel(6, true));
        _balloonLayer.AddChild(_balloonPanel);

        _balloonLabel = HudStyle.Label(15, HorizontalAlignment.Center);
        _balloonLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _balloonLabel.CustomMinimumSize = new Vector2(520, 0);
        _balloonPanel.AddChild(_balloonLabel);
    }

    private void OnNpcSay(int[] textIds)
    {
        string selfName = Net.I.LastEnter.Name ?? "";
        var lines = new System.Collections.Generic.List<string>(textIds.Length);
        foreach (int id in textIds)
        {
            string s = QuestText.Talk(id, selfName);
            if (s.Length > 0) lines.Add(s);
        }
        if (lines.Count == 0) return;

        string text = string.Join("\n", lines);
        _balloonLabel.Text = text;
        _balloonLayer.Visible = true;
        Callable.From(CentreBalloon).CallDeferred();

        int token = ++_balloonToken;
        double secs = Mathf.Clamp(2.5 + text.Length * 0.04, 3.0, 9.0);
        GetTree().CreateTimer(secs).Timeout += () => { if (_balloonToken == token) _balloonLayer.Visible = false; };
    }

    private void CentreBalloon()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        Vector2 sz = _balloonPanel.Size;
        _balloonPanel.Position = new Vector2((vp.X - sz.X) * 0.5f, vp.Y * 0.14f);
    }

    private void OnNpcWindow(GameOpcodes op)
    {
        Chat.Info("This NPC's service isn't available yet.");
    }
}
