using Godot;

namespace LibreKO.Features.Windows;

internal sealed class NpcDialogFeature : IFeature, IClosableWindow, INpcDialog
{
    private readonly IWorldContext context;
    private readonly NpcInteractionFeature npcInteraction;
    private readonly NpcQuestSession npcQuest;
    private readonly QuestBook questBook;
    private readonly SelfSession self;
    private readonly INpcQuestRequests questRequests;
    private readonly ItemTooltipFeature itemTooltip;
    private readonly QuestNotificationsFeature questNotifications;
    private readonly IInteractionDialogs interactionDialogs;

    public NpcDialogFeature(IWorldContext context, NpcQuestSession npcQuest, QuestBook questBook, SelfSession self, INpcQuestRequests npcQuestRequests, ItemTooltipFeature itemTooltip, QuestNotificationsFeature questNotifications, NpcInteractionFeature npcInteraction, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.npcQuest = npcQuest;
        this.questBook = questBook;
        this.self = self;
        questRequests = npcQuestRequests;
        this.itemTooltip = itemTooltip;
        this.questNotifications = questNotifications;
        this.npcInteraction = npcInteraction;
        this.interactionDialogs = interactionDialogs;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private RichTextLabel bodyLabel = null!;
    internal ScrollContainer MenuScroll { get; private set; } = null!;
    private VBoxContainer menuBox = null!;
    internal VBoxContainer QuestContent { get; private set; } = null!;
    private ScrollContainer questScroll = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool bodyEmpty;
    private string script = "";
    private CanvasLayer balloonLayer = null!;
    private PanelContainer balloonPanel = null!;
    private Label balloonLabel = null!;
    private int balloonToken;

    private const int MenuRowsVisible = 15;
    private const float TalkMaxHeight = 200f;
    private const float MenuRowHeight = 39f;

    internal System.Collections.Generic.List<QuestData.Offer> Offers { get; set; } = new();
    private PanelContainer bodyPanel = null!;
    private ScrollContainer bodyScroll = null!;
    private string offerHeader = "";
    private string offerTitle = "";

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildNpcDialog();
        BuildBalloon();
        npcQuest.NpcSayEvent += OnSay;
        npcQuest.NpcSayTextEvent += ShowBalloon;
        npcQuest.NpcMsgEvent += OnMsg;
        npcQuest.QuestViewEvent += OnQuestView;
    }

    void INpcDialog.TrackTalker(int npcCharId) => npcInteraction.TrackTalker(npcCharId);
    void INpcDialog.Balloon(string[] lines) => ShowBalloon(lines);
    void INpcDialog.Show() => Show();
    void INpcDialog.Begin(int npcId, int textId) => Begin(npcId, textId);
    void INpcDialog.Begin(string title, string body) => Begin(title, body);
    void INpcDialog.End(int shownCount) => End(shownCount);
    void INpcDialog.Close() => Close();
    void INpcDialog.AddMenuButton(string label, Action onPressed, string? icon, string tag, bool ready) => AddMenuButton(label, onPressed, icon, tag, ready);
    void INpcDialog.ShowServiceChoice(string body, params (string Label, Action Act)[] options) => ShowServiceChoice(body, options);

    public void Dispose()
    {
        npcQuest.NpcSayEvent -= OnSay;
        npcQuest.NpcSayTextEvent -= ShowBalloon;
        npcQuest.NpcMsgEvent -= OnMsg;
        npcQuest.QuestViewEvent -= OnQuestView;
    }

    internal void ShowPage(NpcDialog dlg)
    {
        script = dlg.ScriptFile;
        if (dlg.HeaderText is { Length: > 0 } body)
            Begin(HeaderName(dlg.NpcId), body);
        else
            Begin(dlg.NpcId, dlg.HeaderTextId);

        int shownCount = 0;
        foreach (var (index, textId, text) in dlg.Buttons)
        {
            string label = text is { Length: > 0 } ? text : QuestText.Menu(textId);
            if (label.Length == 0) label = $"…({textId})";
            int menuIndex = index;
            AddMenuButton($"{++shownCount}.   {label}", () => OnMenuClick(menuIndex));
        }

        if (shownCount == 0 && !bodyEmpty)
            AddMenuButton($"{++shownCount}.   Close", Close);
        End(shownCount);
    }

    internal void BuildNpcDialog()
    {
        layer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(layer);

        Panel = new HudWindow("npc_dialog", "NPC", bodyMinWidth: 420) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        bodyPanel = UiTheme.Section();
        root.AddChild(bodyPanel);
        bodyScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        bodyPanel.AddChild(bodyScroll);

        bodyLabel = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420, 0),
        };
        bodyLabel.AddThemeFontSizeOverride("normal_font_size", 14);
        bodyLabel.AddThemeColorOverride("default_color", UiTheme.TextLo);
        bodyScroll.AddChild(bodyLabel);

        questScroll = new ScrollContainer
        {
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(questScroll);
        QuestContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        QuestContent.AddThemeConstantOverride("separation", 10);
        questScroll.AddChild(QuestContent);

        MenuScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(MenuScroll);
        menuBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        menuBox.AddThemeConstantOverride("separation", 5);
        MenuScroll.AddChild(menuBox);
    }

    // Sub 7's text is the HEADER of the NPC's quest menu; retail builds the list client-side.
    internal void OnMsg(int textId, int npcId)
    {
        Offers = QuestData.OffersAtNpc(
            npcId, questBook.StateOf, self.Sheet.Level, self.Class, self.LastEnter.Nation, context.Zone);
        offerHeader = QuestText.Talk(textId, self.LastEnter.Name ?? "");
        offerTitle = HeaderName(npcId);
        ShowOffers();
    }

    internal void ShowOffers()
    {
        Begin(offerTitle, offerHeader);
        if (Offers.Count == 0)
        {
            if (bodyEmpty) { Close(); return; }
            AddMenuButton("Close", Close);
            End(1);
            return;
        }

        int shownCount = 0;
        foreach (var offer in Offers)
        {
            string name = QuestData.Name(offer.QuestId, self.LastEnter.Name ?? "");
            var (icon, tag) = offer.State switch
            {
                QuestStates.Active => ("system/clock", "In progress"),
                QuestStates.ReadyToTurnIn => ("system/gift", "Ready"),
                _ => ("system/scroll", ""),
            };
            ++shownCount;
            AddMenuButton(name, () => AcceptOffer(offer), icon, tag, offer.State == QuestStates.ReadyToTurnIn);
        }
        End(shownCount);
    }

    private void AcceptOffer(QuestData.Offer offer)
    {
        Close();
        questRequests.QuestAccept(offer.QuestId);
    }

    internal void Begin(int npcId, int textId) =>
        Begin(HeaderName(npcId), QuestText.Talk(textId, self.LastEnter.Name ?? ""));

    private string HeaderName(int npcId) =>
        NpcTitle.Of(NpcNames.Known(npcId),
            context.Entities.Views.TryGetValue(npcQuest.TalkingNpcId, out var talker) && talker.NpcId == npcId ? talker.Name : null, npcId);

    internal void Begin(string title, string body)
    {
        questScroll.Visible = false;
        bodyPanel.Visible = body.Length > 0;
        bodyScroll.ScrollVertical = 0;
        Panel.Title = QuestMarkup.Plain(title, self.LastEnter.Name ?? "");
        bodyLabel.Visible = body.Length > 0;
        bodyLabel.Text = QuestMarkup.Rich(body, self.LastEnter.Name ?? "");
        bodyEmpty = body.Length == 0;

        foreach (var c in menuBox.GetChildren()) { menuBox.RemoveChild(c); c.QueueFree(); }
    }

    internal void ShowServiceChoice(string body, params (string Label, System.Action Act)[] options)
    {
        Begin(npcQuest.LastNpcName, body);
        int shownCount = 0;
        foreach (var (label, act) in options)
        {
            var run = act;
            AddMenuButton($"{++shownCount}.   {label}", () => { Close(); run(); });
        }
        AddMenuButton($"{++shownCount}.   Close", Close);
        End(shownCount);
    }

    private const int ChoiceIconSize = 16;
    private const int ChoiceTagFont = 11;
    private const int ChoiceTagInset = 10;

    private static readonly string[] ReadyColourStates = { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "icon_normal_color", "icon_hover_color" };

    internal void AddMenuButton(string label, System.Action onPressed, string? icon = null, string tag = "", bool ready = false)
    {
        var (caption, readyTopic) = NpcChoice.SplitReady(NpcChoice.Caption(QuestMarkup.Plain(label, self.LastEnter.Name ?? "")));
        var b = new Button
        {
            Text = caption,
            TooltipText = caption,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            Icon = UiIcons.Get(icon ?? NpcChoice.Icon(NpcChoice.Classify(caption))),
            IconAlignment = HorizontalAlignment.Left,
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(0, 34),
        };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.AddThemeConstantOverride("icon_max_width", ChoiceIconSize);
        b.AddThemeColorOverride("icon_normal_color", UiTheme.Gold);
        b.AddThemeColorOverride("icon_hover_color", UiTheme.GoldBright);
        if (ready || readyTopic)
            foreach (var state in ReadyColourStates) b.AddThemeColorOverride(state, UiTheme.Good);
        if (tag.Length > 0)
        {
            var tagLabel = UiTheme.Text(tag, ChoiceTagFont, UiTheme.GoldBright, HorizontalAlignment.Right);
            tagLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            tagLabel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
            tagLabel.OffsetRight = -ChoiceTagInset;
            tagLabel.GrowHorizontal = Control.GrowDirection.Begin;
            tagLabel.VerticalAlignment = VerticalAlignment.Center;
            b.AddChild(tagLabel);
        }
        b.Pressed += () => onPressed();
        menuBox.AddChild(b);
    }

    internal void End(int shownCount)
    {
        MenuScroll.Visible = shownCount > 0;
        MenuScroll.CustomMinimumSize = new Vector2(0,
            Mathf.Min(shownCount, MenuRowsVisible) * MenuRowHeight
            + (shownCount > MenuRowsVisible ? MenuRowHeight * 0.45f : 0f));

        if (bodyEmpty && shownCount == 0) { Close(); return; }
        Show();
        Callable.From(FitTalk).CallDeferred();
    }

    private void FitTalk()
    {
        if (!GodotObject.IsInstanceValid(Panel) || !bodyPanel.Visible) return;
        bodyScroll.CustomMinimumSize = new Vector2(0, Mathf.Min(bodyLabel.GetContentHeight(), TalkMaxHeight));
        Panel.ResetSize();
    }

    private void OnMenuClick(int menuIndex)
    {
        questRequests.SelectMsg(menuIndex, script, -1);
        Close();
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        itemTooltip.Hide();
    }

    private void BuildBalloon()
    {
        balloonLayer = new CanvasLayer { Layer = UiLayers.Announcements, Visible = false };
        context.Root.AddChild(balloonLayer);

        balloonPanel = new PanelContainer();
        balloonPanel.AddThemeStyleboxOverride("panel", UiTheme.Panel(6, true));
        balloonLayer.AddChild(balloonPanel);

        balloonLabel = HudStyle.Label(15, HorizontalAlignment.Center);
        balloonLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        balloonLabel.CustomMinimumSize = new Vector2(520, 0);
        balloonPanel.AddChild(balloonLabel);
    }

    private void OnSay(int[] textIds)
    {
        string selfName = self.LastEnter.Name ?? "";
        var lines = new System.Collections.Generic.List<string>(textIds.Length);
        foreach (int id in textIds)
        {
            string s = QuestText.Talk(id, selfName);
            if (s.Length > 0) lines.Add(s);
        }
        ShowBalloon(lines.ToArray());
    }

    internal void ShowBalloon(string[] lines)
    {
        if (lines.Length == 0) return;

        string text = string.Join("\n", lines);
        balloonLabel.Text = text;
        balloonLayer.Visible = true;
        Callable.From(CentreBalloon).CallDeferred();

        int token = ++balloonToken;
        double secs = Mathf.Clamp(2.5 + text.Length * 0.04, 3.0, 9.0);
        context.Root.After(secs, () => { if (balloonToken == token) balloonLayer.Visible = false; });
    }

    private void CentreBalloon()
    {
        Vector2 vp = context.Root.GetViewport().GetVisibleRect().Size;
        Vector2 sz = balloonPanel.Size;
        balloonPanel.Position = new Vector2((vp.X - sz.X) * 0.5f, vp.Y * 0.14f);
    }

    private int questRewardChoice = -1;

    internal void OnQuestView(QuestView view)
    {
        if (questNotifications.QuestNotifications.Any(q => q.QuestId == view.QuestId && q.State != view.State)
            || view.ZoneId != 0 && view.ZoneId != context.Zone)
        {
            questNotifications.Forget(view.QuestId);
        }
        if (view.Notification)
            questNotifications.ShowNotification(view);
        else if (view.Open && view.Page == QuestPageKind.Conversation)
            ShowQuestConversation(view);
        else if (view.Open)
            ShowQuestView(view);
    }

    private void ShowQuestConversation(QuestView view)
    {
        Begin(NpcNames.Name(view.NpcId, false),
            view.Dialogue);
        int shownCount = 0;
        for (var index = 0; index < view.Topics.Length; index++)
        {
            var offered = index;
            AddMenuButton($"{++shownCount}.   {view.Topics[index]}", () => OnMenuClick(offered));
        }
        AddMenuButton($"{++shownCount}.   Close", Close);
        End(shownCount);
    }

    internal void ShowQuestView(QuestView view)
    {
        questRewardChoice = questBook.RewardSelection.ChoiceIndex(view.QuestId, view.Options);
        Begin(NpcNames.Name(view.NpcId, false), "");
        bodyPanel.Visible = false;
        questScroll.Visible = true;
        questScroll.ScrollVertical = 0;
        foreach (var child in QuestContent.GetChildren()) { QuestContent.RemoveChild(child); child.QueueFree(); }
        QuestContent.AddChild(QuestTitleLine(view));
        QuestContent.AddChild(QuestRows.Paragraph(
            view.Dialogue.Length > 0 ? view.Dialogue : view.Journal, UiTheme.TextHi, self.LastEnter.Name));

        if (view.Objectives.Groups.Length > 0)
        {
            var hunt = QuestRows.Section(QuestContent, QuestRows.ObjectiveHeading(view.Objectives.Groups.Length > 0, false));
            if (view.Objectives.AnyWillDo)
                hunt.AddChild(UiTheme.Text("Complete any one", 12, UiTheme.TextLo));
            for (var index = 0; index < view.Objectives.Groups.Length; index++)
            {
                var goal = view.Objectives.Groups[index];
                var name = goal.Name ?? NpcNames.Name(goal.Monsters[0], true);
                var huntRow = new VBoxContainer();
                huntRow.AddThemeConstantOverride("separation", 3);
                huntRow.AddChild(UiKit.ValueRow(name, $"{view.Counts[index]} / {goal.Count}",
                    view.Counts[index] >= goal.Count ? UiTheme.Good : UiTheme.GoldBright));
                huntRow.AddChild(UiKit.HuntBar(view.Counts[index], goal.Count));
                hunt.AddChild(QuestRows.ObjectiveTarget(huntRow, view.QuestId, index, goal.HasTarget, questRequests));
            }
        }
        var deliveries = view.Transfers.Where(t => t.Take).ToArray();
        if (deliveries.Length > 0)
        {
            var collect = QuestRows.Section(QuestContent, QuestRows.ObjectiveHeading(false, true));
            foreach (var transfer in deliveries)
            {
                var held = transfer.Kind switch
                {
                    0 => self.Inventory.CountOf(transfer.ItemId),
                    1 => self.Sheet.Gold,
                    _ => -1
                };
                collect.AddChild(QuestRows.ItemRow(context.Tooltips, transfer.DisplayItemId, QuestTransferName(transfer),
                    held < 0 ? $"× {transfer.Count:n0}" : $"{Mathf.Min(held, transfer.Count):n0} / {transfer.Count:n0}",
                    held >= transfer.Count ? UiTheme.Good : UiTheme.GoldBright));
            }
        }
        if (view.Objectives.Groups.Length == 0 && deliveries.Length == 0)
            QuestRows.Section(QuestContent, "Objectives").AddChild(QuestRows.Paragraph(view.StandingObjective, UiTheme.TextLo, self.LastEnter.Name));

        var payouts = view.Transfers.Where(t => !t.Take).ToArray();
        if (payouts.Length > 0)
        {
            var rewards = QuestRows.Section(QuestContent, "Rewards");
            foreach (var transfer in payouts)
                rewards.AddChild(QuestRows.ItemRow(context.Tooltips, transfer.DisplayItemId, QuestTransferName(transfer),
                    transfer.Kind is 4 or 5 ? "" : transfer.Count.ToString("n0"), UiTheme.GoldBright));
        }
        System.Action? onRewardChosen = null;
        if (view.Options.Length > 0 && !view.CanClaim)
        {
            var options = QuestRows.Section(QuestContent, "Choose one");
            foreach (var option in view.Options)
                options.AddChild(QuestRows.ItemRow(context.Tooltips, option.DisplayItemId, QuestTransferName(option),
                    option.Count.ToString("n0"), UiTheme.GoldBright));
        }
        else if (view.Options.Length > 0)
        {
            var choice = QuestRows.Section(QuestContent, "Choose one");
            var marks = new List<Label>();
            var frames = new List<PanelContainer>();
            for (var index = 0; index < view.Options.Length; index++)
            {
                var option = view.Options[index];
                var row = QuestRows.ItemRow(context.Tooltips, option.DisplayItemId, QuestTransferName(option),
                    $"{option.Count:n0}  ○", UiTheme.GoldBright);
                marks.Add(row.GetChild<Label>(row.GetChildCount() - 1));
                frames.Add(row.GetChild<PanelContainer>(0));
                choice.AddChild(QuestRewardOption(row, index, () =>
                {
                    questBook.RewardSelection.Choose(view.QuestId, option);
                    onRewardChosen?.Invoke();
                }));
            }
            void PaintChoice()
            {
                for (var index = 0; index < marks.Count; index++)
                {
                    var chosen = index == questRewardChoice;
                    marks[index].Text = $"{view.Options[index].Count:n0}  {(chosen ? "●" : "○")}";
                    marks[index].AddThemeColorOverride("font_color", chosen ? UiTheme.Good : UiTheme.GoldBright);
                    frames[index].AddThemeStyleboxOverride("panel", QuestRows.IconFrame(view.Options[index].DisplayItemId, chosen));
                }
            }
            onRewardChosen = PaintChoice;
            PaintChoice();
        }
        if (view.Daily && view.State == QuestViewState.Completed && view.NextReset > 0)
            QuestContent.AddChild(QuestRows.Paragraph($"Available again: {DateTimeOffset.FromUnixTimeSeconds(view.NextReset).ToLocalTime():g}", UiTheme.TextLo, self.LastEnter.Name));

        for (var index = 0; index < view.Topics.Length; index++)
        {
            var offered = index;
            AddMenuButton(view.Topics[index], () => OnMenuClick(offered));
        }
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        menuBox.AddChild(actions);
        Button Action(string label, System.Action run, bool enabled = true)
        {
            var button = new Button { Text = label, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = !enabled };
            if (!enabled) button.TooltipText = ChooseRewardFirst;
            button.Pressed += () => { Close(); run(); };
            actions.AddChild(button);
            return button;
        }
        if (view.CanAccept) Action("Accept", () => questRequests.QuestAccept(view.QuestId));
        if (view.CanClaim)
        {
            var confirm = Action("Confirm", () => questRequests.QuestComplete(view.QuestId, questRewardChoice),
                view.Options.Length == 0 || questRewardChoice >= 0);
            var paint = onRewardChosen;
            onRewardChosen = () =>
            {
                paint?.Invoke();
                confirm.Disabled = questRewardChoice < 0;
                confirm.TooltipText = confirm.Disabled ? ChooseRewardFirst : "";
            };
        }
        if (!view.CanClaim && view.State is QuestViewState.InProgress or QuestViewState.Claimable)
            Action("Abandon", () => questRequests.QuestAbandon(view.QuestId));
        Action(view.CanAccept ? "Reject" : "Close", () => { });
        End(view.Topics.Length + 1);
        questScroll.CustomMinimumSize = Vector2.Zero;
        ((SceneTree)Engine.GetMainLoop()).Connect(SceneTree.SignalName.ProcessFrame, Callable.From(FitQuestPanel), (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void FitQuestPanel()
    {
        if (!GodotObject.IsInstanceValid(Panel) || !Panel.IsInsideTree() || !questScroll.Visible) return;
        var viewport = Panel.GetViewportRect().Size;
        var room = Mathf.Max(100, viewport.Y - 80 - MenuScroll.CustomMinimumSize.Y);
        questScroll.CustomMinimumSize = new Vector2(0, Mathf.Min(QuestContent.GetCombinedMinimumSize().Y, room));
        Panel.ResetSize();
        Panel.Position = new Vector2(Mathf.Clamp(Panel.Position.X, 0, Mathf.Max(0, viewport.X - Panel.Size.X)),
            Mathf.Clamp(Panel.Position.Y, 0, Mathf.Max(0, viewport.Y - Panel.Size.Y)));
    }

    private Control QuestRewardOption(Control row, int index, System.Action chosen)
    {
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.TooltipText = "Pick this reward";
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
            row.AcceptEvent();
            if (!click.Pressed) return;
            questRewardChoice = index;
            chosen();
        };
        return row;
    }

    private const int TextChooseRewardFirst = 7435;

    private static string ChooseRewardFirst =>
        ItemData.Text(TextChooseRewardFirst, "First select the item you would like to receive as your reward.");

    private const int TextQuestUnderWay = 7422;
    private const int TextQuestBeforeReward = 7423;
    private const int TextQuestComplete = 7424;

    private Control QuestTitleLine(QuestView view)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        var title = UiTheme.Text(view.Title, 15, UiTheme.GoldBright);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(title);
        (int TextId, string Fallback, Color Colour)? state = view.State switch
        {
            QuestViewState.InProgress => (TextQuestUnderWay, "[Under way]", UiTheme.TextLo),
            QuestViewState.Claimable => (TextQuestBeforeReward, "[Before compensation]", UiTheme.GoldBright),
            QuestViewState.Completed => (TextQuestComplete, "[Complete]", UiTheme.Good),
            _ => null,
        };
        if (state is { } word)
            line.AddChild(UiTheme.Text(ItemData.Text(word.TextId, word.Fallback), 12, word.Colour, HorizontalAlignment.Right));
        return line;
    }

    private static string QuestTransferName(QuestTransfer transfer) => transfer.Kind switch
    {
        4 => "First job change",
        5 => "Second job change",
        _ => UiKit.QuestRewardName(transfer.DisplayItemId)
    };
}
