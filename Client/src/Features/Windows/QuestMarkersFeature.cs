using Godot;

namespace LibreKO.Features.Windows;

internal sealed class QuestMarkersFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly QuestBook book;
    private readonly EntitySystem entities;

    public QuestMarkersFeature(IWorldContext context, QuestBook questBook, EntitySystem entities)
    {
        this.context = context;
        book = questBook;
        this.entities = entities;
    }

    private readonly Dictionary<int, string> viewMarkerByNpc = new();

    private readonly HashSet<int> questsWithViews = new();

    public void Build()
    {
        entities.EntitySpawned += OnEntitySpawned;
    }

    public void Dispose()
    {
        entities.EntitySpawned -= OnEntitySpawned;
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info) => RefreshNpcMarker(e);

    internal void RefreshNpcMarkers()
    {
        book.RebuildTargets(context.Zone);
        RebuildViewMarkers();
        foreach (var ent in context.Entities.Views.Values) RefreshNpcMarker(ent);
    }

    private void RebuildViewMarkers()
    {
        viewMarkerByNpc.Clear();
        questsWithViews.Clear();
        foreach (var quest in book.Entries)
        {
            if (!book.Views.TryGetValue(quest.QuestId, out var view)) continue;
            questsWithViews.Add(quest.QuestId);
            if (view.ZoneId != 0 && view.ZoneId != context.Zone) continue;
            viewMarkerByNpc.TryGetValue(view.NpcId, out string? current);
            if (current == NpcRoleFx.ReadyMarker) continue;
            if (view.State == QuestViewState.Claimable)
                viewMarkerByNpc[view.NpcId] = NpcRoleFx.ReadyMarker;
            else if (string.IsNullOrEmpty(current) && view.State is QuestViewState.Available or QuestViewState.InProgress)
                viewMarkerByNpc[view.NpcId] = view.State == QuestViewState.Available ? NpcRoleFx.AvailableMarker : NpcRoleFx.InProgressMarker;
        }
    }

    private void RefreshNpcMarker(EntityView ent)
    {
        if (!ent.IsNpc || ent.IsMonster || ent.NpcId <= 0) return;
        if (NpcRoleFx.HasRole(ent.NpcId)) return;
        string desired = viewMarkerByNpc.TryGetValue(ent.NpcId, out string? fromView) ? fromView : "";

        foreach (var quest in Config.LegacyQuestFallback && desired != NpcRoleFx.ReadyMarker ? book.Entries : [])
        {
            if (questsWithViews.Contains(quest.QuestId)) continue;
            if (quest.State == QuestStates.ReadyToTurnIn
                && (QuestData.NpcForState(quest.QuestId, QuestStates.Completed, ent.NpcId)
                    || QuestData.NpcForState(quest.QuestId, QuestStates.ReadyToTurnIn, ent.NpcId)))
            {
                desired = NpcRoleFx.ReadyMarker;
                break;
            }
            if (desired.Length == 0 && quest.State == QuestStates.Active
                && (QuestData.NpcForState(quest.QuestId, QuestStates.Active, ent.NpcId)
                    || QuestData.NpcForState(quest.QuestId, QuestStates.Completed, ent.NpcId)))
                desired = NpcRoleFx.InProgressMarker;
        }

        if (desired.Length == 0)
        {
            foreach (int questId in QuestData.StartingQuestsForNpc(ent.NpcId))
                if (!book.Views.ContainsKey(questId)
                    && !book.Entries.Any(q => q.QuestId == questId
                        && q.State is QuestStates.Active or QuestStates.Completed or QuestStates.ReadyToTurnIn))
                {
                    desired = NpcRoleFx.AvailableMarker;
                    break;
                }
        }

        if (desired == ent.IndicatorName) return;
        if (ent.IndicatorFx != null && GodotObject.IsInstanceValid(ent.IndicatorFx))
            ent.IndicatorFx.QueueFree();
        ent.IndicatorFx = desired.Length > 0 ? NpcRoleFx.SpawnIndicator(ent, desired) : null;
        ent.IndicatorName = desired;
    }
}
