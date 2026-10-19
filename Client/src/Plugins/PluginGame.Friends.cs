using Godot;

namespace LibreKO.Plugins;

public readonly record struct GameFriend(string Name, bool Online, bool InParty, int Level, string ClassName, string Zone);

public interface IGameFriends
{
    IReadOnlyList<GameFriend> Entries { get; }

    void Refresh();
    void Add();
    void Remove(string name);
    void Whisper(string name);
    void InviteToParty(string name);
    void ShowPlayerMenu(string name, Vector2 at);

    event Action? Changed;
}

public sealed partial class PluginGame
{
    public IGameFriends Friends => friends;

    private readonly FriendsProxy friends = new();

    internal void AttachFriends(IGameFriends friendsSource)
    {
        friends.Source = friendsSource;
        RaiseFriends();
    }

    internal void RaiseFriends() => friends.Raise();

    private sealed class FriendsProxy : IGameFriends
    {
        public IGameFriends? Source;
        public event Action? Changed;
        internal void Raise() => Changed?.Invoke();

        public IReadOnlyList<GameFriend> Entries => Source?.Entries ?? Array.Empty<GameFriend>();
        public void Refresh() => Source?.Refresh();
        public void Add() => Source?.Add();
        public void Remove(string name) => Source?.Remove(name);
        public void Whisper(string name) => Source?.Whisper(name);
        public void InviteToParty(string name) => Source?.InviteToParty(name);
        public void ShowPlayerMenu(string name, Vector2 at) => Source?.ShowPlayerMenu(name, at);
    }
}
