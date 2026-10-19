using Godot;

namespace LibreKO.Features.Hud;

internal sealed class PartyInvitesFeature : IFeature
{
    internal const int InvitedText = 3411;
    private const int InviteAskText = 3415;
    private const string InviteByNameTitle = "Party Invite";
    private const string InviteByNamePrompt = "Invite player by name:";
    private const string InviteNamePlaceholder = "Character name";
    private const string InviteSendText = "Send";
    private const int CharacterNameMax = 20;

    private readonly IWorldContext context;
    private readonly PartySession party;
    private readonly UserOptionSession userOption;
    private readonly IPartyRequests requests;

    private CanvasLayer dialogLayer = null!;
    private ConfirmationDialog inviteAskDialog = null!;
    private bool invitePending;
    private int pendingInviterId = -1;
    private string pendingInviterName = "";
    private IInviteAutoAccept? inviteAutoAccept;

    public PartyInvitesFeature(IWorldContext context, PartySession party, UserOptionSession userOption, IPartyRequests requests)
    {
        this.context = context;
        this.party = party;
        this.userOption = userOption;
        this.requests = requests;
    }

    public void Build()
    {
        BuildPrompts();
        party.PartyErrorEvent += OnError;
        party.PartyInviteEvent += OnInvite;
        party.PartyBbsInviteEvent += OnBbsInvite;
    }

    public void Dispose()
    {
        party.PartyErrorEvent -= OnError;
        party.PartyInviteEvent -= OnInvite;
        party.PartyBbsInviteEvent -= OnBbsInvite;
    }

    internal void AttachInviteAutoAccept(IInviteAutoAccept inviteAutoAccept) => this.inviteAutoAccept = inviteAutoAccept;

    internal void DetachInviteAutoAccept() => inviteAutoAccept = null;

    internal void BuildPrompts()
    {
        dialogLayer = new CanvasLayer { Layer = UiLayers.MainWindows };
        context.Root.AddChild(dialogLayer);
        inviteAskDialog = new ConfirmationDialog
        {
            Title = "Party Invite",
            OkButtonText = "Join",
            Exclusive = false,
        };
        inviteAskDialog.GetCancelButton().Text = "Decline";
        inviteAskDialog.Confirmed += () => AnswerInvite(true);
        inviteAskDialog.Canceled += () => AnswerInvite(false);
        dialogLayer.AddChild(inviteAskDialog);
    }

    internal void OpenInvitePrompt() =>
        Notice.AskText(context.Root, InviteByNamePrompt, ConfirmInvite, InviteByNameTitle, placeholder: InviteNamePlaceholder,
            maxLength: CharacterNameMax, confirmText: InviteSendText);

    private void ConfirmInvite(string typed)
    {
        string name = typed.Trim();
        if (name.Length == 0) return;
        if (party.InParty) requests.Invite(name);
        else requests.Create(name);
        context.Log.Notice($"{name} {ItemData.Text(InvitedText, "was invited into the party. Waiting for a response.")}");
    }

    internal void AcceptPendingInviteIf(Func<int, string, bool> accepts)
    {
        if (!invitePending || !accepts(pendingInviterId, pendingInviterName)) return;
        AnswerInvite(true);
        inviteAskDialog.Hide();
    }

    private void OnInvite(int inviterId, string inviterName)
    {
        if (userOption.PartyRequestsBlocked)
        {
            context.Log.Refusal(CommandCatalog.PartyRequestsRefusedText, "Has blocked party requests.");
            requests.Answer(false);
            return;
        }
        pendingInviterId = inviterId;
        pendingInviterName = inviterName;
        if (inviteAutoAccept?.AcceptInvite(inviterId, inviterName) == true)
        {
            invitePending = false;
            inviteAskDialog.Hide();
            requests.Answer(true);
            return;
        }
        invitePending = true;
        string ask = $"{inviterName} {ItemData.Text(InviteAskText, "has invited you to join the party. Will you join?")}";
        inviteAskDialog.DialogText = ask;
        inviteAskDialog.PopupCentered();
        context.Log.Notice(ask);
    }

    private void OnBbsInvite(PartyBoardInvite invite)
    {
        if (userOption.PartyRequestsBlocked || party.InParty) return;
        var board = party.PartyBoardMode;
        string text = invite.Prompt(ItemData.Text(PartyBoardInvite.PromptText, PartyBoardInvite.PromptFallback), CharacterClassCatalog.DisplayName(invite.Class));
        Notice.Confirm(context.Root, text, "Yes", "No", () => requests.BbsAccept(invite.Inviter, board), title: "Party");
    }

    private void AnswerInvite(bool accept)
    {
        if (!invitePending) return;
        invitePending = false;
        pendingInviterId = -1;
        pendingInviterName = "";
        requests.Answer(accept);
    }

    private void OnError(int code)
    {
        context.Log.Notice(ItemData.Text(PartyInviteRefusal.TextId(code), PartyInviteRefusal.Fallback(code)));
    }
}
