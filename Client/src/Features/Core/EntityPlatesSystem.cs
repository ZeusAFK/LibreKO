using Godot;
using LibreKO.Features.PluginBridge;

namespace LibreKO.Features.Core;

internal sealed class EntityPlatesSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly SiegeSession siege;
    private readonly PartySession party;
    private readonly FriendsSession friends;
    private readonly EntityLooksSystem entityLooks;
    private readonly PluginBridgeFeature pluginBridge;

    public EntityPlatesSystem(IWorldContext context, ClanSession clan, SelfSession self, SiegeSession siege, SelfAvatar avatar, PartySession party, FriendsSession friends, EntityLooksSystem entityLooks, PluginBridgeFeature pluginBridge)
    {
        this.context = context;
        this.clan = clan;
        this.self = self;
        this.siege = siege;
        this.avatar = avatar;
        this.party = party;
        this.friends = friends;
        this.entityLooks = entityLooks;
        this.pluginBridge = pluginBridge;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal event Action<int>? ClanFameApplied;

    internal event Action? PartyPlatesRefreshed;

    internal void ClanLooksInit()
    {
        clan.MyClanChangedEvent += OnSelfClanChanged;
        clan.ClanStandingEvent += OnClanStandingLooks;
        clan.EntityClanEvent += OnEntityClan;
        clan.EntityClanClearedEvent += OnEntityClanCleared;
        clan.AuthorityFameEvent += OnAuthorityFame;
        clan.ClanFameEvent += OnClanFameLooks;
    }

    internal void ClanLooksDispose()
    {
        clan.MyClanChangedEvent -= OnSelfClanChanged;
        clan.ClanStandingEvent -= OnClanStandingLooks;
        clan.EntityClanEvent -= OnEntityClan;
        clan.EntityClanClearedEvent -= OnEntityClanCleared;
        clan.AuthorityFameEvent -= OnAuthorityFame;
        clan.ClanFameEvent -= OnClanFameLooks;
    }

    private void OnSelfClanChanged(MyClanInfo info)
    {
        RefreshSelfClanLine();
        if (context.Self.Body != null)
            StateLooks.DressClanGauntlet(context.Self.Body, self.LastEnter.Race, info.InClan ? info.Grade : 0, info.InClan ? info.Ranking : 0);
    }

    private void OnClanStandingLooks(List<ClanStanding> standings)
    {
        foreach (var standing in standings)
        {
            foreach (var (id, ent) in context.Entities.Views)
            {
                if (ent.IsNpc || ent.KnightsId != standing.ClanId) continue;
                ent.ClanGrade = standing.Grade;
                ent.ClanRanking = standing.Ranking;
                StateLooks.DressClanGauntlet(ent.Body, ent.Race, standing.Grade, standing.Ranking);
            }
        }
    }

    private void OnEntityClan(EntityClanChange change)
    {
        if (change.CharId == context.Self.CharId || !context.Entities.Views.TryGetValue(change.CharId, out var ent)) return;
        ent.KnightsId = change.ClanId;
        ent.ClanGrade = change.Grade;
        ent.ClanRanking = change.Ranking;
        ent.ClanName = change.Name;
        ent.ClanFame = change.Fame;
        RefreshClanLine(change.CharId);
        if (change.CapeId >= 0)
        {
            ent.CapeId = change.CapeId; ent.CapeR = change.Colour & 0xFF; ent.CapeG = (change.Colour >> 8) & 0xFF; ent.CapeB = (change.Colour >> 16) & 0xFF;
            entityLooks.Dress(ent.Body, ent.CapeId, ent.CapeR, ent.CapeG, ent.CapeB, ent.IsGm, ent.Race);
        }
        StateLooks.DressClanGauntlet(ent.Body, ent.Race, change.Grade, change.Ranking);
    }

    private void OnEntityClanCleared(int charId)
    {
        if (charId == context.Self.CharId || !context.Entities.Views.TryGetValue(charId, out var ent)) return;
        ent.KnightsId = 0;
        ent.ClanGrade = 0;
        ent.ClanRanking = 0;
        ent.ClanName = "";
        ent.ClanFlag = 0;
        ent.Plate?.SetMark(null);
        ent.CapeId = 0;
        ent.ClanFame = ClanRanks.None;
        RefreshClanLine(charId);
        entityLooks.Dress(ent.Body, 0, 0, 0, 0, ent.IsGm, ent.Race);
        StateLooks.DressClanGauntlet(ent.Body, ent.Race, 0, 0);
    }

    private void OnAuthorityFame(int charId, byte fame)
    {
        if (charId == context.Self.CharId)
        {
            RefreshSelfClanLine();
            return;
        }
        if (!context.Entities.Views.TryGetValue(charId, out var famed)) return;
        famed.ClanFame = fame;
        RefreshClanLine(famed, charId);
        ClanFameApplied?.Invoke(charId);
    }

    private void OnClanFameLooks(int charId, int clanId, byte fame)
    {
        if (charId == context.Self.CharId) return;
        if (clanId == 0) { OnEntityClanCleared(charId); return; }
        if (!context.Entities.Views.TryGetValue(charId, out var famed)) return;
        famed.ClanFame = fame;
        ClanFameApplied?.Invoke(charId);
    }

    private static readonly Color SiegeDefenseColour = new(0f, 0f, 1f);
    private static readonly Color SiegeAttackColour = new(1f, 0f, 0f);
    private static readonly Color SiegeRefusalColour = new("ff7f50");

    internal void SiegeNamesInit()
    {
        siege.SiegeWarEvent += OnSiegeWarNames;
        OnSiegeWarNames();
    }

    internal void SiegeNamesDispose() => siege.SiegeWarEvent -= OnSiegeWarNames;

    private void OnSiegeWarNames()
    {
        foreach (var (id, e) in context.Entities.Views)
        {
            if (e.IsNpc) continue;
            ApplySiegeName(e);
            RefreshClanLine(e, id);
        }
        pluginBridge.NotifyTarget();
    }

    internal bool AtSiegeWar => SiegeWar.At(context.Zone, siege.SiegeWar);

    internal bool RefusedInSiegeWar()
    {
        if (!AtSiegeWar) return false;
        context.Log.Notice(ItemData.Text(SiegeWar.RefusedInWarText, SiegeWar.Fallback(SiegeWar.RefusedInWarText)), SiegeRefusalColour);
        return true;
    }

    private static string SiegeText(int textId) => ItemData.Text(textId, SiegeWar.Fallback(textId));

    internal string ShownName(EntityView e)
    {
        if (e.IsNpc || !AtSiegeWar) return e.Name;
        int mask = SiegeWar.TargetMaskText(self.IsGm, clan.MyClan.InClan ? clan.MyClan.ClanId : 0, clan.MyClan.InClan ? clan.MyClan.AllianceId : 0,
            e.KnightsId, e.AllianceId, siege.SiegeWar);
        return mask == 0 ? e.Name : SiegeText(mask);
    }

    private (string Text, Color Colour)? SiegeClanLine(EntityView e)
    {
        if (e.IsNpc || !AtSiegeWar) return null;
        bool owner = SiegeWar.OwnerSide(e.KnightsId, e.AllianceId, siege.SiegeWar);
        return (SiegeText(owner ? SiegeWar.DefenseClanText : SiegeWar.SiegeClanText), owner ? SiegeDefenseColour : SiegeAttackColour);
    }

    internal void ApplySiegeName(EntityView e)
    {
        if (e.NameTag == null || !GodotObject.IsInstanceValid(e.NameTag)) return;
        if (!AtSiegeWar)
        {
            int hidden = PlateRules.AnonymousNameText(context.Zone, self.Nation, clan.MyClan.InClan ? clan.MyClan.ClanId : 0, e.Nation, e.KnightsId);
            e.NameTag.Text = hidden > 0 ? ItemData.Text(hidden, e.Name) : e.Name;
            RefreshPlayerNameColour(e);
            return;
        }
        bool owner = SiegeWar.OwnerSide(e.KnightsId, e.AllianceId, siege.SiegeWar);
        e.NameTag.Text = "[" + SiegeText(owner ? SiegeWar.DefenseMemberText : SiegeWar.SiegeMemberText) + "]" + e.Name;
        NamePlate.Tint(e.NameTag, owner ? SiegeDefenseColour : SiegeAttackColour);
    }

    private const int TextNoahPlateLine = 40100;
    private const int TextNoahGraduationNear = 40103;
    private const int TextNoahGraduated = 40104;
    private static readonly Color NoahGraduationColour = new("ffff00");

    internal bool SelfIsNoahKnight => NoahKnights.IsMember(self.Sheet.Level, clan.MyClan.InClan ? clan.MyClan.Fame : ClanRanks.None);

    internal void RefreshClanLine(int charId)
    {
        if (charId == context.Self.CharId) RefreshSelfClanLine();
        else if (context.Entities.Views.TryGetValue(charId, out var ent)) RefreshClanLine(ent, charId);
    }

    internal void RefreshClanLine(EntityView ent, int charId)
    {
        if (ent.IsNpc) return;
        if (SiegeClanLine(ent) is { } siegeLine) { ent.Plate?.SetClan(siegeLine.Text, siegeLine.Colour); return; }
        ApplyClanLine(ent.Plate, ent.ClanName, ent.Level, ent.ClanFame, HasStall(charId));
    }

    private IStallOwners? stallOwners;

    internal void AttachStallOwners(IStallOwners stallOwners) => this.stallOwners = stallOwners;

    internal void DetachStallOwners() => stallOwners = null;

    internal bool HasStall(int charId) => stallOwners?.HasStall(charId) == true;

    internal void RefreshSelfClanLine()
    {
        context.Chat?.NoahStatusChanged();
        pluginBridge.Adapters?.Character.RaiseCharacter();
        var plate = SelfPlate();
        ApplyClanLine(plate, clan.MyClan.InClan ? clan.MyClan.Name : "", self.Sheet.Level,
            clan.MyClan.InClan ? clan.MyClan.Fame : ClanRanks.None, HasStall(context.Self.CharId));
        plate?.SetNameMark(NameMark.For(clan.MyClan.InClan ? clan.MyClan.Fame : ClanRanks.None, clan.MyClan.InClan ? clan.MyClan.Flag : 0, context.Zone, true));
    }

    private void ApplyClanLine(PlateStack? plate, string clanName, int level, int clanDuty, bool hasStall)
    {
        if (plate == null) return;
        if (NameMark.HidesClan(self.LastEnter.Zone)) plate.SetClan("");
        else if (NoahKnights.ShowsPlateLine(clanName, level, clanDuty, hasStall))
            plate.SetClan(ItemData.Text(TextNoahPlateLine, "[Noah Knights]"), NoahKnights.PlateColour);
        else
            plate.SetClan(clanName);
    }

    internal void NoticeNoahGraduation(int oldLevel, int level)
    {
        if (oldLevel <= 0 || level == oldLevel) return;
        if (level == NoahKnights.MaxLevel)
            context.Log.Notice(ItemData.Text(TextNoahGraduationNear,
                "Starting from level 51 you graduate from the Noah Knights and become a freelance"), NoahGraduationColour);
        else if (level == NoahKnights.GraduationLevel)
            Notice.Show(context.Root, ItemData.Text(TextNoahGraduated,
                "Congratulations for graduating from the Noah Knights. \n May your path be straight and peaceful."));
    }

    internal HeadJoint SelfHead { get; } = new();

    internal void FitPlateToLook(int charId)
    {
        Label3D? tag;
        HeadJoint joint;
        if (charId == context.Self.CharId)
        {
            tag = avatar.NameTag;
            joint = SelfHead;
        }
        else if (context.Entities.Views.TryGetValue(charId, out var e))
        {
            tag = e.NameTag;
            joint = e.Head;
        }
        else return;
        if (context.Entities.BodyOf(charId) is not { } host || tag == null || !GodotObject.IsInstanceValid(tag)) return;
        if (!tag.HasMeta(PlateFollow.PlateBaseYMeta)) tag.SetMeta(PlateFollow.PlateBaseYMeta, tag.Position.Y);
        float y = tag.GetMeta(PlateFollow.PlateBaseYMeta).AsSingle();
        if (host.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is { } worn && worn.HasMeta(PlateFollow.WornPlateYMeta))
            y = worn.GetMeta(PlateFollow.WornPlateYMeta).AsSingle();
        var at = new Vector3(tag.Position.X, y, tag.Position.Z);
        if (PlateOf(charId) is { } plate && plate.Name == tag) plate.MoveTo(at);
        else tag.Position = at;
        joint.Forget();
    }

    internal Node3D? SelfPlateRoot()
    {
        return avatar.NameTag?.GetParent() as Node3D;
    }

    private PlateStack? selfPlate;

    internal void OnNamePlatesRescaled()
    {
        selfPlate?.Rescaled();
        foreach (var ent in context.Entities.Views.Values) ent.Plate?.Rescaled();
    }

    internal PlateStack? SelfPlate()
    {
        if (selfPlate != null) return selfPlate;
        return avatar.NameTag is { } tag ? selfPlate = new PlateStack(tag) : null;
    }

    internal float HeadHeightOf(int charId)
    {
        bool sitting = charId == context.Self.CharId
            ? (avatar.Sitting || HasStall(charId))
            : (context.Entities.Views.TryGetValue(charId, out var sitEnt) && (sitEnt.Sitting || HasStall(charId)));

        if (charId != context.Self.CharId)
        {
            float baseH = context.Entities.Views.TryGetValue(charId, out var tagEnt) && tagEnt.NameTag is { } tag
                ? Mathf.Max(0.4f, tag.Position.Y)
                : NamePlate.StandingHeight;
            return sitting ? Mathf.Min(baseH, NamePlate.SittingHeight) : baseH;
        }
        float selfH = Mathf.Max(0.4f, avatar.NameTag?.Position.Y ?? NamePlate.StandingSelfHeight);
        return sitting ? Mathf.Min(selfH, NamePlate.SittingHeight) : selfH;
    }

    internal int AnonymousNameText(EntitySnapshot info) =>
        PlateRules.AnonymousNameText(context.Zone, self.Nation, clan.MyClan.InClan ? clan.MyClan.ClanId : 0, info.Nation, info.KnightsId);

    internal void RefreshPlayerNameColours()
    {
        foreach (var e in context.Entities.Views.Values)
            if (AtSiegeWar) ApplySiegeName(e);
            else RefreshPlayerNameColour(e);
    }

    internal void RefreshPartyPlates()
    {
        if (AtSiegeWar) return;
        var partyColour = PartyPlateColour;
        foreach (var e in context.Entities.Views.Values)
        {
            if (e.IsNpc || e.NameTag == null || !GodotObject.IsInstanceValid(e.NameTag)) continue;
            bool member = IsPartyPlate(e.Id);
            if (member == NamePlate.TintOf(e.NameTag).IsEqualApprox(partyColour)) continue;
            if (member) NamePlate.Tint(e.NameTag, partyColour);
            else RefreshPlayerNameColour(e);
        }
        PartyPlatesRefreshed?.Invoke();
    }

    internal void RefreshPlayerNameColour(EntityView e)
    {
        if (e.IsNpc || e.NameTag == null || !GodotObject.IsInstanceValid(e.NameTag)) return;
        NamePlate.Tint(e.NameTag, IsPartyPlate(e.Id) ? PartyPlateColour
            : PlateRules.FriendColour(context.Zone) && friends.IsFriend(e.Name) ? FriendNameColour
            : PlateRules.AnonymousNameText(context.Zone, self.Nation, clan.MyClan.InClan ? clan.MyClan.ClanId : 0, e.Nation, e.KnightsId)
                == PlateRules.DelosHeroText ? NamePlate.Enemy
            : PlayerNameColor(e.Nation));
    }

    private Color PlayerNameColor(int nation) =>
        nation == self.Nation ? NamePlate.Ally : NamePlate.Enemy;

    private static readonly Color FriendNameColour = new(0.5f, 1f, 0.5f);

    private Color PartyPlateColour => new(PlateRules.PartyNameColour(context.Zone));

    private bool IsPartyPlate(int charId) => charId != context.Self.CharId && InMyParty(charId);

    internal Color NameColor(EntitySnapshot info)
    {
        if (!info.IsNpc && IsPartyPlate(info.Id)) return PartyPlateColour;
        if (!info.IsNpc && PlateRules.FriendColour(context.Zone) && friends.IsFriend(info.Name)) return FriendNameColour;
        if (!info.IsNpc && AnonymousNameText(info) == PlateRules.DelosHeroText) return NamePlate.Enemy;
        if (!info.IsNpc && ZoneNameText(info) == ChatSender.PathoistText) return PathoistColour;
        if (!info.IsNpc) return PlayerNameColor(ShownNation(info));
        if (!info.Attackable) return PeacefulNpcColour;
        int d = info.Level - self.Sheet.Level;
        if (d >= StrongMonsterLevelGap) return StrongMonsterColour;
        if (d >= 0) return EvenMonsterColour;
        return WeakMonsterColour;
    }

    internal static readonly Color PathoistColour = new(1f, 0f, 0f);
    private const int StrongMonsterLevelGap = 4;
    private static readonly Color PeacefulNpcColour = new(0.55f, 0.85f, 1f);
    private static readonly Color StrongMonsterColour = new(1f, 0.35f, 0.30f);
    private static readonly Color EvenMonsterColour = new(1f, 0.85f, 0.35f);
    private static readonly Color WeakMonsterColour = new(0.5f, 1f, 0.5f);

    internal int ZoneNameText(EntitySnapshot info)
    {
        int text = ZoneLooks.OtherNameText(context.Zone, self.Nation, self.IsGm, party.InParty, InMyParty(info.Id));
        return text > 0 ? text : AnonymousNameText(info);
    }

    internal bool InMyParty(int charId)
    {
        foreach (var member in party.Members)
            if (member.CharId == charId) return true;
        return false;
    }

    internal int ShownNation(EntitySnapshot info) =>
        !info.IsNpc && ZoneLooks.IsDeityZone(context.Zone) ? (self.Nation == Nations.ElMorad ? Nations.Karus : Nations.ElMorad) : info.Nation;

    internal void ApplySelfZoneName()
    {
        int text = ZoneLooks.SelfNameText(context.Zone, self.Nation);
        if (text > 0) SelfPlate()?.SetNameText(ItemData.Text(text, self.LastEnter.Name ?? ""));
    }

    internal PlateStack? PlateOf(int id) =>
        id == context.Self.CharId ? SelfPlate() : context.Entities.Views.TryGetValue(id, out var e) ? e.Plate : null;

    internal bool PlatesHidden { get; private set; }

    internal void ToggleNamePlates()
    {
        PlatesHidden = !PlatesHidden;
        SelfPlate()?.SetVisible(!PlatesHidden);
    }
}
