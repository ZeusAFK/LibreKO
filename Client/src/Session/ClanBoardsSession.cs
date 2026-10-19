namespace LibreKO.Session;

public sealed class ClanBoardsSession
{
    public event Action<ClanBoardStatus>? ClanBoardStatusEvent;

    internal void RaiseClanBoardStatus(ClanBoardStatus clanBoardStatus) => ClanBoardStatusEvent?.Invoke(clanBoardStatus);

    public event Action<List<ClanAdvert>>? ClanAdvertsEvent;

    internal void RaiseClanAdverts(List<ClanAdvert> list) => ClanAdvertsEvent?.Invoke(list);

    public event Action<int, int>? ClanApplyResultEvent;

    internal void RaiseClanApplyResult(int arg1, int arg2) => ClanApplyResultEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? ClanAdvertResultEvent;

    internal void RaiseClanAdvertResult(int arg1, int arg2) => ClanAdvertResultEvent?.Invoke(arg1, arg2);

    public event Action<int, string, string>? ClanPromoteEvent;

    internal void RaiseClanPromote(int arg1, string arg2, string arg3) => ClanPromoteEvent?.Invoke(arg1, arg2, arg3);

    public event Action<List<WishPost>>? WishPostsEvent;

    internal void RaiseWishPosts(List<WishPost> list) => WishPostsEvent?.Invoke(list);

    public event Action<WishStatus>? WishStatusEvent;

    internal void RaiseWishStatus(WishStatus wishStatus) => WishStatusEvent?.Invoke(wishStatus);

    public event Action? WishTooSoonEvent;

    internal void RaiseWishTooSoon() => WishTooSoonEvent?.Invoke();

    public event Action<List<WishRequest>>? WishRequestsEvent;

    internal void RaiseWishRequests(List<WishRequest> list) => WishRequestsEvent?.Invoke(list);

    public event Action<ClanBoardAction, int>? WishIntroResultEvent;

    internal void RaiseWishIntroResult(int arg1, int arg2) => WishIntroResultEvent?.Invoke((ClanBoardAction)arg1, arg2);

    public event Action<ClanBoardAction, int>? WishRequestResultEvent;

    internal void RaiseWishRequestResult(int arg1, int arg2) => WishRequestResultEvent?.Invoke((ClanBoardAction)arg1, arg2);

    public event Action<int, string>? WishPromoteEvent;

    internal void RaiseWishPromote(int arg1, string arg2) => WishPromoteEvent?.Invoke(arg1, arg2);
}
