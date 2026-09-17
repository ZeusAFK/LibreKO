using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _challengeLayer = null!;
    private ConfirmationDialog _challengeAskDialog = null!;

    private bool _challengeRequestPending;
    private bool _challengeOutgoing;
    private string _challengeOpponent = "Player";

    private const float ChallengeRange = 20f;

    private void ChallengeInit()
    {
        BuildChallengePrompt();
        Net.I.ChallengeRequestEvent += OnChallengeRequest;
        Net.I.ChallengeSentEvent += OnChallengeSent;
        Net.I.ChallengeCancelledEvent += OnChallengeCancelled;
        Net.I.ChallengeRejectedEvent += OnChallengeRejected;
        Net.I.ChallengeErrorEvent += OnChallengeError;
    }

    private void ChallengeDispose()
    {
        Net.I.ChallengeRequestEvent -= OnChallengeRequest;
        Net.I.ChallengeSentEvent -= OnChallengeSent;
        Net.I.ChallengeCancelledEvent -= OnChallengeCancelled;
        Net.I.ChallengeRejectedEvent -= OnChallengeRejected;
        Net.I.ChallengeErrorEvent -= OnChallengeError;
    }

    private void BuildChallengePrompt()
    {
        _challengeLayer = new CanvasLayer { Layer = 76 };
        AddChild(_challengeLayer);

        _challengeAskDialog = new ConfirmationDialog { Title = "Duel challenge" };
        _challengeAskDialog.GetOkButton().Text = "Accept";
        _challengeAskDialog.GetCancelButton().Text = "Decline";
        _challengeAskDialog.Confirmed += () => AnswerChallenge(true);
        _challengeAskDialog.Canceled += () => AnswerChallenge(false);
        _challengeLayer.AddChild(_challengeAskDialog);
    }

    private void ChallengeNearest()
    {
        if (!_worldReady) return;
        if (_selfDead) return;
        if (_challengeOutgoing) return;
        if (_challengeRequestPending) return;

        int bestId = -1; float bestD = ChallengeRange;
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (e.IsNpc || e.Dead || kv.Key == _myId) continue;
            if (_self == null) break;
            float d = e.Body.Position.DistanceTo(_self.Position);
            if (d < bestD) { bestD = d; bestId = kv.Key; }
        }
        if (bestId < 0) { Chat.Info("No player nearby to challenge."); return; }

        _challengeOpponent = _ents.TryGetValue(bestId, out var pe) && pe.Name.Length > 0 ? pe.Name : "Player";
        ChallengeByName(_challengeOpponent);
    }

    private void ChallengeByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        if (_selfDead || _challengeOutgoing || _challengeRequestPending) return;
        _challengeOpponent = targetName;
        Net.I.SendChallengeRequest(targetName);
        Chat.Info($"Challenging {targetName} to a duel…");
    }

    private void ChallengeCancelOutgoing()
    {
        if (!_challengeOutgoing) return;
        Net.I.SendChallengeCancel();
        _challengeOutgoing = false;
        Chat.Info("You cancelled the duel challenge.");
    }

    private void OnChallengeSent(string targetName)
    {
        _challengeOpponent = targetName;
        _challengeOutgoing = true;
        Chat.Info($"Waiting for {targetName} to accept the duel…");
    }

    private void OnChallengeRejected()
    {
        _challengeOutgoing = false;
        Chat.Info($"{_challengeOpponent} declined the duel.");
    }

    private void OnChallengeError()
    {
        _challengeOutgoing = false;
        Chat.Info("The duel challenge failed (target unavailable).");
    }

    private void OnChallengeRequest(string challengerName)
    {
        if (_challengeRequestPending || _challengeOutgoing) { Net.I.SendChallengeReject(); return; }
        _challengeOpponent = string.IsNullOrEmpty(challengerName) ? "Someone" : challengerName;
        _challengeRequestPending = true;
        _challengeAskDialog.DialogText = $"{_challengeOpponent} challenges you to a duel.\nAccept and warp to the arena?";
        _challengeAskDialog.PopupCentered();
        Chat.Info($"{_challengeOpponent} challenges you to a duel.");
    }

    private void AnswerChallenge(bool accept)
    {
        if (!_challengeRequestPending) return;
        _challengeRequestPending = false;
        if (accept)
        {
            Net.I.SendChallengeAccept();
            Chat.Info($"You accepted {_challengeOpponent}'s duel — warping to the arena…");
        }
        else
        {
            Net.I.SendChallengeReject();
            Chat.Info($"You declined {_challengeOpponent}'s duel.");
        }
    }

    private void OnChallengeCancelled()
    {
        if (_challengeRequestPending)
        {
            _challengeRequestPending = false;
            _challengeAskDialog.Hide();
        }
        _challengeOutgoing = false;
        Chat.Info($"{_challengeOpponent} cancelled the duel challenge.");
    }
}
