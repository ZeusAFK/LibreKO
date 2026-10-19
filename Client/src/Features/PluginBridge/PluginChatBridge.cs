namespace LibreKO.Features.PluginBridge;

internal sealed class PluginChatBridge(Func<ChatSystem?> chat) : IGameChat
{
    event Action<string>? IGameChat.LineAdded { add { } remove { } }

    event Action<GameChatLine>? IGameChat.LineArrived { add { } remove { } }

    event Action? IGameChat.InputRequested { add { } remove { } }

    IReadOnlyList<string> IGameChat.History => chat()?.History ?? Array.Empty<string>();

    IReadOnlyList<GameChatLine> IGameChat.Lines => chat()?.Lines ?? Array.Empty<GameChatLine>();

    public void Send(string text) => chat()?.SendText(text);

    public void SetTyping(bool typing)
    {
        chat()?.NotePluginTyping(typing);
    }
}
