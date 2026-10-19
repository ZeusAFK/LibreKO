using Godot;

namespace LibreKO.Features.Windows;

internal sealed class KrowazFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;

    public KrowazFeature(IWorldContext context, ChatSession chat)
    {
        this.context = context;
        this.chat = chat;
    }

    private const int TextTeleport = 7667;
    private static readonly Color NoticeColour = new(1f, 1f, 0x32 / 255f);

    public void Build() => chat.KrowazNoticeEvent += OnNotice;

    public void Dispose() => chat.KrowazNoticeEvent -= OnNotice;

    private void OnNotice(int seconds) =>
        context.Log.Notice(TextTemplate.Fill(GameText.System(TextTeleport, "You will be teleported to Crowaz in %d seconds"), seconds),
            NoticeColour);
}
