using Godot;

namespace LibreKO.Features.Social;

internal sealed class ChatReactionsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly RoomMediaSession roomMedia;
    private readonly IRoomMediaRequests roomMediaRequests;
    private readonly PlayerMenuFeature playerMenu;

    public ChatReactionsFeature(IWorldContext context, RoomMediaSession roomMedia, IRoomMediaRequests roomMediaRequests, PlayerMenuFeature playerMenu)
    {
        this.context = context;
        this.roomMedia = roomMedia;
        this.roomMediaRequests = roomMediaRequests;
        this.playerMenu = playerMenu;
    }

    internal CanvasLayer ReactionLayer { get; private set; } = null!;
    internal ReactionBar? ReactionBar { get; private set; }
    internal ReactionDetails? ReactionDetails { get; private set; }
    private EmoticonPicker? reactionPicker;
    private RoomPost? reactionPost;
    private ChatThreadView? reactionThread;
    private Rect2 reactionAnchor;

    public void Build()
    {
        ReactionLayer = new CanvasLayer { Layer = UiLayers.FloatingPanels };
        ReactionLayer.SetMeta(Audio.QuietMeta, true);
        context.Root.AddChild(ReactionLayer);
        roomMedia.ChatPostChangedEvent += OnPostChanged;
    }

    public void Dispose()
    {
        roomMedia.ChatPostChangedEvent -= OnPostChanged;
    }

    internal void WireThread(ChatThreadView thread)
    {
        thread.NameClicked += playerMenu.ShowByName;
        thread.ItemClicked += context.Tooltips.ShowItem;
        thread.ItemHoverEnded += context.Tooltips.Hide;
        thread.ReactRequested += (post, anchor) => OpenReactionBar(thread, post, anchor);
        thread.ReactionsClicked += (post, anchor) => OpenReactionDetails(thread, post, anchor);
    }

    internal void EnsureReactionPopups()
    {
        if (ReactionBar != null) return;
        ReactionBar = new ReactionBar();
        ReactionBar.Picked += React;
        ReactionBar.More += OpenReactionPicker;
        ReactionBar.PopupHide += ReleaseReactionPin;
        ReactionLayer.AddChild(ReactionBar);

        ReactionDetails = new ReactionDetails();
        ReactionDetails.AddRequested += () =>
        {
            if (reactionThread != null && reactionPost != null)
            {
                reactionThread.Pinned = reactionPost;
                reactionThread.ShowReactButton(reactionPost);
            }
            ReactionBar.ShowAbove(reactionAnchor, reactionPost?.Reactions.Of(context.Self.CharId) ?? "");
        };
        ReactionDetails.RemoveMine += () =>
        {
            React("");
            ReactionDetails.Hide();
        };
        ReactionLayer.AddChild(ReactionDetails);

        reactionPicker = new EmoticonPicker();
        reactionPicker.Picked += React;
        reactionPicker.PopupHide += ReleaseReactionPin;
        ReactionLayer.AddChild(reactionPicker);
    }

    internal void OpenReactionBar(ChatThreadView thread, RoomPost post, Control anchor)
    {
        EnsureReactionPopups();
        if (reactionThread != null && !ReferenceEquals(reactionThread, thread)) reactionThread.Unpin();
        reactionThread = thread;
        reactionPost = post;
        reactionAnchor = anchor.GetGlobalRect();
        ReactionBar!.ShowAbove(reactionAnchor, post.Reactions.Of(context.Self.CharId));
    }

    internal void OpenReactionDetails(ChatThreadView thread, RoomPost post, Control anchor)
    {
        EnsureReactionPopups();
        reactionThread = thread;
        reactionPost = post;
        reactionAnchor = anchor.GetGlobalRect();
        ReactionDetails!.ShowFor(post, context.Self.CharId, reactionAnchor);
    }

    private void OpenReactionPicker()
    {
        if (reactionPicker == null) return;
        reactionThread!.Pinned = reactionPost;
        reactionPicker.ShowAt(reactionAnchor.Position);
    }

    private void ReleaseReactionPin()
    {
        if (reactionPicker is { Visible: true } || ReactionBar is { Visible: true }) return;
        reactionThread?.Unpin();
    }

    internal void React(string emoji)
    {
        if (reactionPost is not { Id: > 0 } post) return;
        roomMediaRequests.Reaction(post.Id, ReactionRules.Toggle(post.Reactions.Of(context.Self.CharId), emoji));
    }

    private void OnPostChanged(RoomPost post)
    {
        if (ReactionDetails is { Visible: true } details && ReferenceEquals(details.Post, post)) details.Refresh();
    }
}
