namespace LibreKO.Session;

public sealed class FriendsSession
{
    private readonly List<FriendEntry> friends = new();

    public IReadOnlyList<FriendEntry> Friends => friends;

    public bool IsFriend(string name) => friends.Exists(friend => string.Equals(friend.Name, name, StringComparison.OrdinalIgnoreCase));

    public event Action<List<FriendEntry>>? FriendListEvent;

    internal void ApplyList(IEnumerable<FriendEntry> entries)
    {
        friends.Clear();
        friends.AddRange(entries);
        RaiseFriendList();
    }

    internal void ApplyAdded(FriendEntry entry)
    {
        friends.RemoveAll(friend => string.Equals(friend.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        friends.Add(entry);
        RaiseFriendList();
    }

    internal void ApplyRemoved(string name)
    {
        if (friends.RemoveAll(friend => string.Equals(friend.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
            RaiseFriendList();
    }

    internal void Clear() => friends.Clear();

    internal void ApplyDetails(IEnumerable<FriendDetails> details)
    {
        foreach (var detail in details)
        {
            int index = friends.FindIndex(friend => string.Equals(friend.Name, detail.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            var entry = friends[index];
            entry.Level = detail.Level;
            entry.Class = detail.Class;
            entry.Nation = detail.Nation;
            entry.ZoneId = detail.ZoneId;
            friends[index] = entry;
        }
        RaiseFriendList();
    }

    private void RaiseFriendList() => FriendListEvent?.Invoke(new List<FriendEntry>(friends));

    public event Action<FriendAddResult, string, FriendEntry>? FriendAddResultEvent;

    internal void RaiseFriendAddResult(FriendAddResult result, string name, FriendEntry friendEntry) => FriendAddResultEvent?.Invoke(result, name, friendEntry);

    public event Action<FriendRemoveResult, string>? FriendRemoveResultEvent;

    internal void RaiseFriendRemoveResult(FriendRemoveResult result, string name) => FriendRemoveResultEvent?.Invoke(result, name);

    public event Action<string>? FriendLoggedInEvent;

    internal void RaiseFriendLoggedIn(string value) => FriendLoggedInEvent?.Invoke(value);

    public event Action<string>? FriendRequestEvent;

    internal void RaiseFriendRequest(string value) => FriendRequestEvent?.Invoke(value);

    public event Action<FriendRequestResult>? FriendRequestResultEvent;

    internal void RaiseFriendRequestResult(FriendRequestResult result) => FriendRequestResultEvent?.Invoke(result);
}
