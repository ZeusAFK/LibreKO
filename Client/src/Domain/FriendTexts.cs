namespace LibreKO.Domain;

public static class FriendTexts
{
    public const int RequestSentText = 10405;
    public const int AddedText = 10406;
    public const int LoggedInText = 10404;
    public const int RequestAskText = 44454;
    public const double RequestSeconds = 10.0;

    private const int AddFailedText = 10400;
    private const int AddListFullText = 10401;
    private const int RemoveFailedText = 10402;
    private const int RemoveNotOnListText = 10403;
    private const int FirstRequestResultText = 44561;

    public static int? AddResultText(FriendAddResult code) => code switch
    {
        FriendAddResult.Succeeded => AddedText,
        FriendAddResult.ListFull => AddListFullText,
        _ => AddFailedText,
    };

    public static int? RemoveResultText(FriendRemoveResult code) => code switch
    {
        FriendRemoveResult.Succeeded => null,
        FriendRemoveResult.NotOnTheList => RemoveNotOnListText,
        _ => RemoveFailedText,
    };

    public static int? RequestResultText(FriendRequestResult code) =>
        code is >= FriendRequestResult.ListFull and <= FriendRequestResult.Denied
            ? FirstRequestResultText + (code - FriendRequestResult.ListFull)
            : null;
}
