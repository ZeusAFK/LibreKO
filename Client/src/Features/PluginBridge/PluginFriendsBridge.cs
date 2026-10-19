using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginFriendsBridge(FriendsFeature friends, FriendsSession friendsSession, WhispersFeature whispers, PlayerMenuFeature playerMenu, Node root, SelfSession self, IFriendsRequests friendsRequests) : IGameFriends
{
    event Action? IGameFriends.Changed { add { } remove { } }

    public IReadOnlyList<GameFriend> Entries
    {
        get
        {
            friends.EnsureLoaded();
            var list = new List<GameFriend>(friendsSession.Friends.Count);
            foreach (var f in friendsSession.Friends)
                list.Add(new GameFriend(f.Name, f.IsOnline, f.InParty, f.Level,
                    f.Class > 0 ? CharacterClassCatalog.DisplayName(f.Class) : "", f.IsOnline ? FriendsFeature.FriendZoneName(f.ZoneId) : ""));
            return list;
        }
    }

    public void Refresh() => friendsRequests.FriendListRequest();

    public void Add() => AskFriendAdd();

    public void Remove(string name) =>
        Notice.Confirm(root, string.Format(FriendRemovePrompt, name), "Remove", "Cancel", () => friendsRequests.FriendRemove(name));

    public void Whisper(string name) => whispers.OpenWhisperWith(name);

    public void InviteToParty(string name) => playerMenu.InviteToParty(name);

    public void ShowPlayerMenu(string name, Vector2 at) => playerMenu.ShowByName(name, at);

    private const string FriendAddTitle = "Friends";

    private const string FriendAddPrompt = "Character name to add:";

    internal const string FriendRemovePrompt = "Remove {0} from your friends?";

    private const int FriendNameMax = 20;

    internal void AskFriendAdd() =>
        Notice.AskText(root, FriendAddPrompt, typed =>
        {
            string name = typed.Trim();
            if (name.Length == 0) return;
            if (name == (self.LastEnter.Name ?? "")) { friends.SetFriendStatus("You can't add yourself.", true); return; }
            friends.RequestFriend(name);
        }, FriendAddTitle, placeholder: "Character name", maxLength: FriendNameMax);
}
