using Godot;

namespace LibreKO.Features.Windows;

internal static class KingMessages
{
    internal static void Show(Node root, string text, string title)
    {
        if (text.Length > 0) Notice.Show(root, text, title);
    }

    internal static void Result(Node root, KingReply reply, short result, string title)
    {
        int id = KingElection.ResultText(reply, result);
        if (id == KingElection.UnknownResult) Show(root, KingElection.UnknownResultLine(result), title);
        else if (id != 0) Show(root, NpcServiceKit.Text(id), title);
    }
}
