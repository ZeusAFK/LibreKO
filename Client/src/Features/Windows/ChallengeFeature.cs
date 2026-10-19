using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ChallengeFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ChallengeSession challenge;
    private readonly IChallengeRequests requests;

    public ChallengeFeature(IWorldContext context, ChallengeSession challenge, IChallengeRequests requests)
    {
        this.context = context;
        this.challenge = challenge;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private ConfirmationDialog askDialog = null!;
    private bool requestPending;
    private bool outgoing;
    private Notice? waitBox;
    private string opponent = "Player";

    private const float Range = 20f;
    private const string Title = "Duel challenge";
    private const int TextChallenged = 10950;
    private const int TextWaiting = 10951;
    private const int TextRejected = 10954;
    private const int TextFailed = 10955;
    private const int TextZone = 10956;
    private const int TextNotLeader = 10957;

    public void Build()
    {
        BuildPrompt();
        challenge.ChallengeRequestEvent += OnRequest;
        challenge.ChallengeSentEvent += OnSent;
        challenge.ChallengeCancelledEvent += OnCancelled;
        challenge.ChallengeRejectedEvent += OnRejected;
        challenge.ChallengeErrorEvent += OnError;
    }

    public void Dispose()
    {
        challenge.ChallengeRequestEvent -= OnRequest;
        challenge.ChallengeSentEvent -= OnSent;
        challenge.ChallengeCancelledEvent -= OnCancelled;
        challenge.ChallengeRejectedEvent -= OnRejected;
        challenge.ChallengeErrorEvent -= OnError;
    }

    private void BuildPrompt()
    {
        layer = new CanvasLayer { Layer = UiLayers.Prompts };
        context.Root.AddChild(layer);

        askDialog = new ConfirmationDialog { Title = Title };
        askDialog.GetOkButton().Text = "Accept";
        askDialog.GetCancelButton().Text = "Decline";
        askDialog.Confirmed += () => Answer(true);
        askDialog.Canceled += () => Answer(false);
        layer.AddChild(askDialog);
    }

    private void Nearest()
    {
        if (!context.Ready) return;
        if (context.Self.Dead) return;
        if (outgoing) return;
        if (requestPending) return;

        int bestId = -1; float bestD = Range;
        foreach (var e in context.Entities.All())
        {
            if (e.IsNpc || e.Dead || e.Id == context.Self.CharId) continue;
            if (context.Self.Body == null) break;
            float d = e.Position.DistanceTo(context.Self.Body.Position);
            if (d < bestD) { bestD = d; bestId = e.Id; }
        }
        if (bestId < 0) { context.Log.Notice("No player nearby to challenge."); return; }

        opponent = context.Entities.Find(bestId)?.Name is { Length: > 0 } name ? name : "Player";
        ByName(opponent);
    }

    internal void ByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        if (context.Self.Dead || outgoing || requestPending) return;
        opponent = targetName;
        requests.ChallengeRequest(targetName);
        context.Log.Notice($"Challenging {targetName} to a duel…");
    }

    private void CancelOutgoing()
    {
        waitBox = null;
        if (!outgoing) return;
        requests.Cancel();
        outgoing = false;
        context.Log.Notice("You cancelled the duel challenge.");
    }

    private void OnSent(string targetName)
    {
        opponent = targetName;
        outgoing = true;
        waitBox = Notice.Waiting(context.Root, ItemData.Text(TextWaiting, "Waiting for a response from %s.").Replace("%s", targetName),
            "Cancel", CancelOutgoing, Title);
    }

    private void EndOutgoing()
    {
        outgoing = false;
        waitBox?.Close();
        waitBox = null;
    }

    private void OnRejected()
    {
        EndOutgoing();
        RefusalLine(TextRejected, "Your challenge has been rejected.");
    }

    private void OnError(ChallengeSubOpcode code)
    {
        EndOutgoing();
        if (code == ChallengeSubOpcode.ZoneError)
            RefusalLine(TextZone, "You cannot challenge a character(clan) in this zone.");
        else if (code == ChallengeSubOpcode.ClanError)
            RefusalLine(TextNotLeader, "This is not a clan leader.");
        else
            RefusalLine(TextFailed, "Challenge request failed.");
    }

    private void RefusalLine(int textId, string fallback) =>
        context.Log.Add(ItemData.Text(textId, fallback), CombatLogKind.Status, UiKit.WhRefusalTint);

    private void OnRequest(string challengerName)
    {
        if (requestPending || outgoing) { requests.Reject(); return; }
        opponent = string.IsNullOrEmpty(challengerName) ? "Someone" : challengerName;
        requestPending = true;
        askDialog.DialogText = ItemData.Text(TextChallenged,
            "%s has challenged you into a battle. Press OK and you'll be automatically be teleported to the battlefield.").Replace("%s", opponent);
        askDialog.PopupCentered();
        context.Log.Notice($"{opponent} challenges you to a duel.");
    }

    private void Answer(bool accept)
    {
        if (!requestPending) return;
        requestPending = false;
        if (accept)
        {
            requests.Accept();
            context.Log.Notice($"You accepted {opponent}'s duel — warping to the arena…");
        }
        else
        {
            requests.Reject();
            context.Log.Notice($"You declined {opponent}'s duel.");
        }
    }

    private void OnCancelled()
    {
        if (requestPending)
        {
            requestPending = false;
            askDialog.Hide();
        }
        EndOutgoing();
        RefusalLine(TextRejected, "Your challenge has been rejected.");
    }
}
