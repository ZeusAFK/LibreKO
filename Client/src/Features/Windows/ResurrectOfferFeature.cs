using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ResurrectOfferFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly CombatSession combat;
    private readonly SelfSession self;
    private readonly ICombatRequests combatRequests;
    private readonly DeathDialogFeature deathDialog;
    private readonly CombatSystem combatSystem;

    public ResurrectOfferFeature(IWorldContext context, CombatSession combat, SelfSession self, ICombatRequests combatRequests, DeathDialogFeature deathDialog, CombatSystem combatSystem)
    {
        this.context = context;
        this.combat = combat;
        this.self = self;
        this.combatRequests = combatRequests;
        this.deathDialog = deathDialog;
        this.combatSystem = combatSystem;
    }

    private const int Text = 44304;
    private const int TextInPlace = 44305;
    private const int TextRebirthStandby = 44300;
    private const int TextRebirthMissingItem = 44301;
    private const int TextRebirthError = 44303;
    private const double Seconds = 30.0;
    private const double ChoiceSeconds = 10.0;
    private const int RespawnFxKarus = 10020;
    private const int RespawnFxElmorad = 10019;
    private const short Offered = 2;
    private const int ResultSlot = 1;

    private Notice? prompt;
    private int token;

    public void Build()
    {
        combatSystem.SelfResurrected += ClosePrompt;
        combatSystem.SelfRespawned += OnSelfRespawned;
        combat.ResurrectOfferEvent += On;
        combat.RegeneRefusedEvent += OnRegeneRefused;
    }

    public void Dispose()
    {
        combatSystem.SelfResurrected -= ClosePrompt;
        combatSystem.SelfRespawned -= OnSelfRespawned;
        combat.ResurrectOfferEvent -= On;
        combat.RegeneRefusedEvent -= OnRegeneRefused;
    }

    private void OnSelfRespawned()
    {
        ClosePrompt();
        SpawnRespawnEffect();
    }

    internal static bool ResurrectionOnlyOffered(SkillData.Skill s, short[] data) =>
        s.IsResurrect && data.Length > ResultSlot && data[ResultSlot] == Offered;

    private void On(int casterId, int skillId)
    {
        if (!context.Self.Dead) return;
        string text = FillInOrder(
            GameText.System(Text, "%s used %s. Would you like to resurrect?"),
            context.Entities.NameOf(casterId), SkillData.Get(skillId)?.Name ?? "");
        ShowTimedPrompt(text, Seconds, accepted =>
        {
            if (accepted) combatRequests.ResurrectAccept(casterId);
            else AskInPlace(casterId);
        });
    }

    private void AskInPlace(int casterId)
    {
        if (!context.Self.Dead) return;
        ShowTimedPrompt(
            GameText.System(TextInPlace,
                "Would you like to resurrect in place? If you cancel, you will resurrect in town."),
            ChoiceSeconds,
            inPlace => combatRequests.ResurrectChoice(inPlace ? ResurrectionChoice.InPlace : ResurrectionChoice.Town, casterId));
    }

    private void ShowTimedPrompt(string text, double seconds, Action<bool> answer)
    {
        ClosePrompt();
        int token = this.token;
        bool answered = false;
        void Answer(bool yes)
        {
            if (answered || token != this.token) return;
            answered = true;
            this.prompt = null;
            answer(yes);
        }
        var prompt = Notice.Confirm(context.Root, text, "OK", "Cancel", () => Answer(true), () => Answer(false));
        this.prompt = prompt;
        context.Root.After(seconds, () =>
        {
            if (answered || token != this.token) return;
            Answer(false);
            if (GodotObject.IsInstanceValid(prompt)) prompt.Close();
        });
    }

    internal void ClosePrompt()
    {
        token++;
        if (prompt != null && GodotObject.IsInstanceValid(prompt)) prompt.Close();
        prompt = null;
    }

    private void OnRegeneRefused(RespawnRefusal code, int itemId, ushort amount)
    {
        string text = code switch
        {
            RespawnRefusal.Standby => GameText.System(TextRebirthStandby, "You are in a standby for the Rebirth."),
            RespawnRefusal.MissingItem => FillInOrder(
                GameText.System(TextRebirthMissingItem, "Player does not have the required item [%s]. Amount Needed : %d"),
                ItemData.DisplayName(itemId), amount.ToString()),
            _ => FillInOrder(GameText.System(TextRebirthError, "Rebirth Error (%d)"), ((ushort)code).ToString()),
        };
        context.Log.Notice(text);
        if (context.Self.Dead) deathDialog.ArmRespawnButton(deathDialog.OffersScrolls ? DeathDialogFeature.ScrollConfirmHint : DeathDialogFeature.RespawnHint);
    }

    internal void SpawnRespawnEffect()
    {
        int fx = self.LastEnter.Nation == Nations.Karus ? RespawnFxKarus : RespawnFxElmorad;
        if (Fx.NameForId(fx) is { } name) context.Entities.SpawnFxOn(context.Self.CharId, name, 0f);
    }

    internal static string FillInOrder(string text, params string[] values)
    {
        foreach (string value in values)
        {
            int at = FirstPlaceholder(text);
            if (at < 0) break;
            text = text[..at] + value + text[(at + 2)..];
        }
        return text;
    }

    private static int FirstPlaceholder(string text)
    {
        int s = text.IndexOf("%s", StringComparison.Ordinal);
        int d = text.IndexOf("%d", StringComparison.Ordinal);
        return s < 0 ? d : d < 0 ? s : Math.Min(s, d);
    }
}
