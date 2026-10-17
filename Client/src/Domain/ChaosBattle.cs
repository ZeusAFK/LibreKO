using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LibreKO.Domain;

public readonly record struct ChaosRankRow(string Name, int Kills, int Deaths);

public readonly record struct ChaosLadderLine(string Name, int Kills, int Deaths, int Rank, bool Winner, bool Loser, bool Own);

public static class ChaosBattle
{
    public const int Zone = 85;
    public const int IndunEvent = 24;
    public const string EventTitle = "Chaos";
    public const int RowsShown = 18;
    public const double RankingThrottleSeconds = 10.0;
    public const short SkillTree = 3000;
    public const int ChaosItemKind = 254;

    public const int StartedText = 18803;
    public const int OverText = 18804;
    public const int PlateOverText = 18805;
    public const int SecondsText = 7446;
    public const int NoAttackText = 18802;
    public const int SkillWindowText = 18809;
    public const int InventoryText = 7436;
    public const int AltKeyText = 18910;
    public const int CubeText = 18911;
    public const int PickaxeJoinText = 18806;
    public const int PickaxeEquipText = 18808;
    public const int PickaxeArmedText = 18807;
    public const string BannerRule = "###########################################";
    public static readonly Color BannerColour = new("f04500");

    public const int KillingBladeItem = 700037000;
    public const int DrainRestoreItem = 700040000;
    public const int LightPitItem = 700041000;

    public static readonly int[] BannerTexts = { NoAttackText, SkillWindowText, InventoryText, AltKeyText, CubeText };
    public static readonly int[] DefaultBar = { 490219, 490220, 490221, 490222, 490223 };
    private const int KeptFirst = 490219;
    private const int KeptLast = 490230;

    public static bool KeptOnEntry(int id) => id is >= KeptFirst and <= KeptLast;

    public static readonly Color WinnerName = new("eff1c9");
    public static readonly Color WinnerRank = new("808080");
    public static readonly Color OtherName = new("c0c0c0");
    public static readonly Color OtherRank = new("ff2828");

    private const double WinnerShare = 0.2;
    private const double WinnerRounding = 0.9;
    private const int PickaxeKindA = 61;
    private const int PickaxeKindB = 63;

    public static bool IsPickaxe(int kind) => kind is PickaxeKindA or PickaxeKindB;

    public static bool ItemUsable(int zone, int kind) => zone != Zone || kind == ChaosItemKind;

    public static bool ShowsCapes(int zone) => zone != Zone;

    public static bool BattleLocked(int zone, int selfAbnormal) => zone == Zone && selfAbnormal == RespawnBlink.Blinking;

    public static int WinnerCut(int count) => Math.Max(1, (int)Math.Floor(count * WinnerShare + WinnerRounding));

    public static List<ChaosLadderLine> Ladder(IReadOnlyList<ChaosRankRow> rows, string ownName)
    {
        var lines = new List<ChaosLadderLine>();
        if (rows.Count is 0 or > RowsShown) return lines;

        var sorted = rows
            .Select((row, index) => (row, index))
            .OrderByDescending(r => r.row.Kills)
            .ThenBy(r => r.row.Deaths)
            .ThenBy(r => r.index)
            .Select(r => r.row)
            .ToList();

        var ranks = new int[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
            ranks[i] = i > 0 && sorted[i].Kills == sorted[i - 1].Kills && sorted[i].Deaths == sorted[i - 1].Deaths
                ? ranks[i - 1]
                : i + 1;

        int cut = WinnerCut(sorted.Count);
        int tiedWithFirst = sorted.Count(r => r.Kills == sorted[0].Kills && r.Deaths == sorted[0].Deaths);
        bool badges = sorted.Count > 1;
        bool winnersAllowed = tiedWithFirst <= (sorted.Count + 1) / 2;
        bool losersShown = sorted[0].Kills > 0;

        for (int i = 0; i < sorted.Count; i++)
        {
            bool winner = badges && winnersAllowed && (ranks[i] < cut || (ranks[i] == ranks[0] && ranks[0] <= cut));
            lines.Add(new ChaosLadderLine(sorted[i].Name, sorted[i].Kills, sorted[i].Deaths, ranks[i],
                winner, badges && !winner && losersShown, sorted[i].Name == ownName));
        }
        return lines;
    }

    public static int MaskedNameText(bool own, int selfNation) =>
        (own ? selfNation == Nations.ElMorad : selfNation != Nations.ElMorad) ? ChatSender.ElMoradDeityText : ChatSender.KarusDeityText;
}
