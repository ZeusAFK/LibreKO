using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MonsterStoneFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly MonsterStoneSession monsterStone;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly IMonsterStoneRequests requests;
    private readonly BasicAttackSystem basicAttack;

    public MonsterStoneFeature(IWorldContext context, MonsterStoneSession monsterStone, PartySession party, SelfSession self, IMonsterStoneRequests requests, BasicAttackSystem basicAttack)
    {
        this.context = context;
        this.monsterStone = monsterStone;
        this.party = party;
        this.self = self;
        this.requests = requests;
        this.basicAttack = basicAttack;
    }

    private const float NestExplainWidth = 230f;
    private const float NestExplainHeight = 160f;
    private const string NestQuestion = "Do you like to Enter?";
    private const string NestTeleportLine = "Teleport";
    private static readonly Color NestLogColour = new(1f, 1f, 0f);

    private CanvasLayer nestLayer = null!;
    internal HudWindow NestWindow { get; private set; } = null!;
    internal Label NestExplain { get; private set; } = null!;
    private Notice? nestExpConfirm;
    internal int NestStone { get; set; }
    private double battleUntil;

    internal void NoteBattle(double now) => battleUntil = now + NestDungeon.BattleSeconds;

    public void Build()
    {
        basicAttack.SelfInBattle += NoteBattle;
        BuildNestWindow();
        monsterStone.MonsterStoneEvent += OnResult;
    }

    public void Dispose()
    {
        basicAttack.SelfInBattle -= NoteBattle;
        monsterStone.MonsterStoneEvent -= OnResult;
        DismissNestExpConfirm();
    }

    internal void BuildNestWindow()
    {
        nestLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(nestLayer);
        NestWindow = new HudWindow("nestindun", "Monster Stone") { Visible = false };
        NestWindow.Closed += CloseNestWindow;
        nestLayer.AddChild(NestWindow);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        NestWindow.Body.AddChild(column);

        NestExplain = HudStyle.Label(13);
        NestExplain.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        NestExplain.VerticalAlignment = VerticalAlignment.Top;
        NestExplain.CustomMinimumSize = new Vector2(NestExplainWidth, NestExplainHeight);
        column.AddChild(NestExplain);

        column.AddChild(NestLine(NestQuestion, UiTheme.TextHi));
        column.AddChild(NestLine(NestTeleportLine, UiTheme.Gold));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 6);
        buttons.AddChild(GenderChangeFeature.LookActionButton("Enter", EnterNest));
        buttons.AddChild(GenderChangeFeature.LookActionButton("Receive EXP", AskNestExp));
        buttons.AddChild(GenderChangeFeature.LookActionButton("Cancel", CloseNestWindow));
        column.AddChild(buttons);
    }

    private static Label NestLine(string text, Color colour)
    {
        var label = UiTheme.Text(text, 13, colour);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    internal static bool OpensNestDungeon(SkillData.Skill? skill) =>
        skill?.CooldownGroup == NestDungeon.EventSummonGroup;

    private bool NestBlockedByBattle() => context.Now < battleUntil || self.Stunned(context.Now);

    private void NestLog(int textId, string fallback) => context.Log.Notice(ItemData.Text(textId, fallback), NestLogColour);

    internal void OpenNestDungeon(int itemId)
    {
        if (party.InParty)
        {
            NestLog(NestDungeon.InPartyText, "You cannot use is on Party Status.");
            return;
        }
        if (context.Self.Dead)
        {
            NestLog(NestDungeon.DeadText, "Monster's Stone cannot be used in death.");
            return;
        }
        if (NestWindow.Visible || self.Inventory.CountInBackpack(itemId) < 1) return;

        NestStone = itemId;
        NestWindow.Title = ItemData.DisplayName(itemId);
        NestExplain.Text = ItemData.Get(itemId)?.Desc ?? "";
        NestWindow.Visible = true;
    }

    private void EnterNest()
    {
        if (NestBlockedByBattle())
        {
            NestLog(NestDungeon.BattleText, "Cannot be used during battle");
            return;
        }
        if (context.Self.Dead)
        {
            NestLog(NestDungeon.DeadText, "Monster's Stone cannot be used in death.");
            return;
        }
        int stone = NestStone;
        CloseNestWindow();
        if (self.Inventory.CountInBackpack(stone) < 1) return;
        requests.Send(stone);
    }

    private void AskNestExp()
    {
        DismissNestExpConfirm();
        string text = ItemData.Text(NestDungeon.ExpQuestionText, "Do you wish to receive EXP using Monster Stone? \n EXP : %d")
            .Replace("%d", NestDungeon.ExpFor(self.Sheet.Level).ToString("n0"));
        int stone = NestStone;
        nestExpConfirm = Notice.Confirm(context.Root, text, "OK", "Cancel", () => ReceiveNestExp(stone), () => nestExpConfirm = null);
    }

    private void ReceiveNestExp(int stone)
    {
        nestExpConfirm = null;
        CloseNestWindow();
        if (self.Inventory.CountInBackpack(stone) < 1) return;
        requests.Exp(stone);
    }

    private void CloseNestWindow()
    {
        NestWindow.Visible = false;
        NestStone = 0;
    }

    private void OnResult(MonsterStoneResult result, int itemId)
    {
        if (NestDungeon.Reply(result, context.Zone) is not { } reply) return;
        string text = ItemData.Text(reply.Id, reply.Fallback);
        if (reply.Boxed) Notice.Show(context.Root, text);
        else context.Log.Notice(text, NestLogColour);
    }

    private void DismissNestExpConfirm()
    {
        if (nestExpConfirm != null && GodotObject.IsInstanceValid(nestExpConfirm))
            nestExpConfirm.Close();
        nestExpConfirm = null;
    }
}
