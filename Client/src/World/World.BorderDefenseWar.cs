using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _bdwHudLayer = null!;
    private WarScoreStrip _bdwScoreBanner = null!;
    private Godot.Timer _bdwTick = null!;

    private const byte BdwZone = 84;
    private const int BdwVictorySound = 340119;
    private const int BdwDefeatSound = 340118;
    private const string BdwCentre = "VS";
    private const string BdwAltarStands = "Altar of Manes: Active in Center";

    private void BorderDefenseWarInit()
    {
        _bdwHudLayer = new CanvasLayer { Layer = 95 };
        AddChild(_bdwHudLayer);

        BuildBdwScoreBanner();

        _bdwTick = new Godot.Timer { WaitTime = 1.0, Autostart = false, OneShot = false };
        _bdwTick.Timeout += RefreshBdwHud;
        _bdwHudLayer.AddChild(_bdwTick);

        Net.I.TempleScreenScoreEvent += OnBdwScoresReceived;
        Net.I.AltarFlagEvent += OnBdwAltarFlagReceived;
        Net.I.AltarTimerEvent += OnBdwAltarTimerReceived;
        Net.I.TempleEventFinishEvent += OnBdwFinishReceived;

        if (_zone != BdwZone)
        {
            Net.I.BorderWar.Reset();
            return;
        }
        _bdwScoreBanner.Visible = true;
        RefreshBdwHud();
    }

    private void BorderDefenseWarDispose()
    {
        Net.I.TempleScreenScoreEvent -= OnBdwScoresReceived;
        Net.I.AltarFlagEvent -= OnBdwAltarFlagReceived;
        Net.I.AltarTimerEvent -= OnBdwAltarTimerReceived;
        Net.I.TempleEventFinishEvent -= OnBdwFinishReceived;

        _bdwTick?.Stop();
        if (_bdwHudLayer != null && IsInstanceValid(_bdwHudLayer))
            _bdwHudLayer.QueueFree();
    }

    private void BuildBdwScoreBanner()
    {
        _bdwScoreBanner = new WarScoreStrip(withStatus: true)
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Centre = BdwCentre,
        };
        _bdwHudLayer.AddChild(_bdwScoreBanner);
        AddEventPlate(_bdwScoreBanner);
    }

    private void OnBdwScoresReceived(int karus, int elmo) => RefreshBdwHud();

    private void OnBdwAltarFlagReceived(string playerName, byte nation) => RefreshBdwHud();

    private void OnBdwAltarTimerReceived(int seconds) => RefreshBdwHud();

    private void OnBdwFinishReceived(int eventId, int winnerNation, uint seconds)
    {
        bool won = winnerNation != 0 && winnerNation == Net.I.LastEnter.Nation;
        Audio.PlayUi(won ? BdwVictorySound : BdwDefeatSound);
        CombatNotice(BdwResultLine(winnerNation));
        RefreshBdwHud();
    }

    private static string BdwResultLine(int winnerNation) =>
        winnerNation == 0 ? "The battle ended in a draw."
        : winnerNation == Net.I.LastEnter.Nation ? "Your nation has won the battle."
        : "Your nation has lost the battle.";

    private void RefreshBdwHud()
    {
        if (_zone != BdwZone || !IsInstanceValid(_bdwScoreBanner))
        {
            _bdwTick.Stop();
            return;
        }

        var war = Net.I.BorderWar;
        var now = DateTime.UtcNow;
        _bdwScoreBanner.SetScores(war.KarusScore, war.ElmoradScore);
        bool counting = false;
        if (war.Winner is { } winner)
        {
            int home = war.HomeSecondsLeft(now);
            var colour = winner == 0 ? UiTheme.TextHi : winner == Net.I.LastEnter.Nation ? UiTheme.Good : UiTheme.Bad;
            _bdwScoreBanner.SetStatus($"{BdwResultLine(winner)}  {home / 60}:{home % 60:00}", colour);
            counting = home > 0;
        }
        else if (war.Carrier.Length > 0)
        {
            ShowBdwCarrier(war.Carrier, war.CarrierNation);
        }
        else if (war.AltarSecondsLeft(now) is > 0 and var altar)
        {
            _bdwScoreBanner.SetStatus($"Altar respawning in {altar}s", UiTheme.GoldBright);
            counting = true;
        }
        else
        {
            _bdwScoreBanner.SetStatus(BdwAltarStands, UiTheme.Good);
        }

        if (!counting) _bdwTick.Stop();
        else if (_bdwTick.IsStopped()) _bdwTick.Start();
    }

    private void ShowBdwCarrier(string carrier, byte nation)
    {
        bool karus = nation == Nations.Karus;
        _bdwScoreBanner.SetStatus($"Carrier: {carrier} ({(karus ? "Karus" : "El Morad")})",
            karus ? WarScoreStrip.KarusColour : WarScoreStrip.ElmoradColour);
    }
}
