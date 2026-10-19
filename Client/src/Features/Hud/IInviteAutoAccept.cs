namespace LibreKO.Features.Hud;

internal interface IInviteAutoAccept
{
    bool AcceptInvite(int inviterId, string name);
}
