using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _rebirthLayer = null!;
    private HudWindow _rebirthPanel = null!;
    private ConfirmationDialog _rebirthAsk = null!;
    private Label _rebirthLevelLbl = null!, _rebirthStatus = null!;
    private Label _rebirthExpReq = null!, _rebirthGoldReq = null!, _rebirthNpReq = null!;
    private Label _rebirthStr = null!, _rebirthSta = null!, _rebirthDex = null!, _rebirthInt = null!, _rebirthMag = null!;
    private Button _rebirthBtn = null!;
    private bool _rebirthShown;
    private bool _rebirthInFlight;

    private int _rebirthLevel;
    private int _rebirthLvl;
    private long _rebirthExp, _rebirthMaxExp;
    private int _rebirthGold, _rebirthNp;
    private int _rebStr, _rebSta, _rebDex, _rebInt, _rebMag;

    private void RebirthInit()
    {
        BuildRebirthPanel();

        _rebirthLvl = Sheet.Level;
        _rebirthExp = Sheet.Exp; _rebirthMaxExp = Sheet.MaxExp;
        SeedRebirthSheet();

        Net.I.RebirthActivateEvent += OnRebirthActivate;
        Net.I.RebirthResultEvent   += OnRebirthResult;
        Net.I.RebirthCompleteEvent += OnRebirthComplete;
        Net.I.RebirthProgressEvent += OnRebirthProgress;
        Net.I.GoldChangeEvent      += OnRebirthGold;
        Net.I.LoyaltyChangeEvent   += OnRebirthLoyalty;
        Net.I.ExpChangeEvent       += OnRebirthExp;
        Net.I.LevelChangeEvent     += OnRebirthLevel;
    }

    private void SeedRebirthSheet()
    {
        _rebirthGold = Sheet.Gold; _rebirthNp = Sheet.Np;
        _rebStr = Sheet.Str; _rebSta = Sheet.Sta; _rebDex = Sheet.Dex;
        _rebInt = Sheet.Intel; _rebMag = Sheet.Mag;
    }

    private void RebirthDispose()
    {
        Net.I.RebirthActivateEvent -= OnRebirthActivate;
        Net.I.RebirthResultEvent   -= OnRebirthResult;
        Net.I.RebirthCompleteEvent -= OnRebirthComplete;
        Net.I.RebirthProgressEvent -= OnRebirthProgress;
        Net.I.GoldChangeEvent      -= OnRebirthGold;
        Net.I.LoyaltyChangeEvent   -= OnRebirthLoyalty;
        Net.I.ExpChangeEvent       -= OnRebirthExp;
        Net.I.LevelChangeEvent     -= OnRebirthLevel;
    }

    private void BuildRebirthPanel()
    {
        _rebirthLayer = new CanvasLayer { Layer = 74 };
        AddChild(_rebirthLayer);

        _rebirthPanel = new HudWindow("rebirth", "Master Rebirth", new Vector2(220, 130), 320) { Visible = false };
        _rebirthPanel.Closed += CloseRebirth;
        _rebirthLayer.AddChild(_rebirthPanel);

        var root = _rebirthPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _rebirthLevelLbl = UiTheme.Text("", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        root.AddChild(_rebirthLevelLbl);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle("Requirements"));
        _rebirthExpReq  = HudStyle.Label(13); root.AddChild(_rebirthExpReq);
        _rebirthGoldReq = HudStyle.Label(13); root.AddChild(_rebirthGoldReq);
        _rebirthNpReq   = HudStyle.Label(13); root.AddChild(_rebirthNpReq);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle("Rebirth bonus (carried from your current stats)"));
        _rebirthStr = HudStyle.Label(13); root.AddChild(_rebirthStr);
        _rebirthSta = HudStyle.Label(13); root.AddChild(_rebirthSta);
        _rebirthDex = HudStyle.Label(13); root.AddChild(_rebirthDex);
        _rebirthInt = HudStyle.Label(13); root.AddChild(_rebirthInt);
        _rebirthMag = HudStyle.Label(13); root.AddChild(_rebirthMag);

        root.AddChild(new HSeparator());
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(actionRow);
        _rebirthBtn = new Button { Text = "Rebirth", FocusMode = Control.FocusModeEnum.None };
        _rebirthBtn.AddThemeFontSizeOverride("font_size", 13);
        _rebirthBtn.Pressed += OnRebirthPressed;
        actionRow.AddChild(_rebirthBtn);
        _rebirthStatus = HudStyle.Label(13, HorizontalAlignment.Right);
        _rebirthStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actionRow.AddChild(_rebirthStatus);

        _rebirthAsk = new ConfirmationDialog { Title = "Rebirth" };
        _rebirthAsk.Confirmed += OnRebirthConfirmed;
        _rebirthLayer.AddChild(_rebirthAsk);
    }

    private void ToggleRebirth()
    {
        if (_rebirthShown) { CloseRebirth(); return; }
        SeedRebirthSheet();

        SetRebirthStatus("", false);
        RefreshRebirthUI();
        _rebirthPanel.Visible = true;
        _rebirthShown = true;
    }

    private void CloseRebirth()
    {
        if (!_rebirthShown) return;
        _rebirthShown = false;
        _rebirthPanel.Visible = false;
    }

    private int RebirthExpPercent() =>
        _rebirthMaxExp > 0 ? (int)Mathf.Min(100, _rebirthExp * 100 / _rebirthMaxExp) : 0;

    private bool RebirthEligible() =>
        RebirthExpPercent() >= 100 && _rebirthGold >= Net.RebirthGoldCost && _rebirthNp >= Net.RebirthLoyaltyCost;

    private void RefreshRebirthUI()
    {
        _rebirthLevelLbl.Text = _rebirthLevel > 0
            ? $"Rebirth Lv {_rebirthLevel}"
            : "Not yet reborn";

        int exp = RebirthExpPercent();
        bool expOk = exp >= 100, goldOk = _rebirthGold >= Net.RebirthGoldCost, npOk = _rebirthNp >= Net.RebirthLoyaltyCost;
        SetReq(_rebirthExpReq,  expOk,  $"EXP at 100%  ({exp}%)");
        SetReq(_rebirthGoldReq, goldOk, $"Gold  {_rebirthGold:n0} / {Net.RebirthGoldCost:n0}");
        SetReq(_rebirthNpReq,   npOk,   $"National points  {_rebirthNp:n0} / {Net.RebirthLoyaltyCost:n0}");

        _rebirthStr.Text = $"STR +{_rebStr}";
        _rebirthSta.Text = $"HP  +{_rebSta}";
        _rebirthDex.Text = $"DEX +{_rebDex}";
        _rebirthInt.Text = $"INT +{_rebInt}";
        _rebirthMag.Text = $"MP  +{_rebMag}";

        _rebirthBtn.Disabled = _rebirthInFlight || !RebirthEligible();
    }

    private static void SetReq(Label lbl, bool met, string text)
    {
        lbl.Text = (met ? "✓  " : "✕  ") + text;
        lbl.AddThemeColorOverride("font_color", met ? UiTheme.Good : UiTheme.Bad);
    }

    private void SetRebirthStatus(string text, bool warn)
    {
        _rebirthStatus.Text = text;
        _rebirthStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : Colors.White);
    }

    private void OnRebirthPressed()
    {
        if (_rebirthInFlight || _selfDead) return;
        if (!RebirthEligible())
        {
            SetRebirthStatus("You don't meet the requirements yet.", true);
            return;
        }
        _rebirthAsk.DialogText =
            $"Rebirth your character?\n\nCost: {Net.RebirthGoldCost:n0} gold + {Net.RebirthLoyaltyCost:n0} NP" +
            "\nYour EXP will be reset to 0 and your current stats become a permanent bonus.";
        _rebirthAsk.PopupCentered();
    }

    private void OnRebirthConfirmed()
    {
        if (_rebirthInFlight || _selfDead) return;
        if (!RebirthEligible())
        {
            SetRebirthStatus("You don't meet the requirements yet.", true);
            return;
        }
        _rebirthInFlight = true;
        SetRebirthStatus("Reincarnating…", false);
        RefreshRebirthUI();
        Net.I.SendRebirthRequest();
    }

    private void OnRebirthActivate()
    {
        _rebirthInFlight = false;
        SetRebirthStatus("Rebirth accepted!", false);
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthResult(int code)
    {
        _rebirthInFlight = false;
        SetRebirthStatus("Rebirth failed — you no longer meet the requirements.", true);
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthComplete(int rebirthLevel)
    {
        _rebirthLevel = rebirthLevel;
        _rebirthInFlight = false;
        Chat.Info($"Rebirth complete — you are now Rebirth Lv {rebirthLevel}.");
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthProgress(int levelOffset, int current, int max)
    {
        if (!_rebirthShown) return;
        int pct = max > 0 ? Mathf.Clamp((int)((long)current * 100 / max), 0, 100) : 0;
        SetRebirthStatus($"Reincarnating… {pct}%", false);
    }

    private void OnRebirthGold(int total)
    {
        _rebirthGold = total;
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthLoyalty(int np, int monthly)
    {
        _rebirthNp = np;
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthExp(long exp)
    {
        _rebirthExp = exp;
        if (_rebirthShown) RefreshRebirthUI();
    }

    private void OnRebirthLevel(int level, int statPoints, int skillPool, long maxExp, long exp,
        int maxHp, int hp, int maxMp, int mp)
    {
        _rebirthLvl = level;
        _rebirthMaxExp = maxExp;
        _rebirthExp = exp;
        if (_rebirthShown) RefreshRebirthUI();
    }
}
