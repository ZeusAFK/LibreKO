namespace LibreKO.Features.PluginBridge;

internal sealed class PluginSkillsBridge(SkillCastSystem skillCast, SkillWindowFeature skillWindow, HotbarFeature hotbar, SelfSession self) : IGameSkills
{
    event Action? IGameSkills.Changed { add { } remove { } }

    public IReadOnlyList<GameSkillTab> Tabs =>
        SkillData.Pages(self.Class, skillCast.SelfTransformModel(), SkillData.Get(self.TransformSkill), self.Nation).Select(p => new GameSkillTab(p.Category, p.Label)).ToList();

    public int MasteryPool => self.Mastery.Pool;

    public IReadOnlyList<GameMasteryTree> Trees
    {
        get
        {
            var list = new List<GameMasteryTree>();
            for (int type = MasteryPoints.FirstTree; type <= MasteryPoints.LastTree; type++)
            {
                bool shown = type != MasteryPoints.MasterTree || MasteryPoints.ClassHasTree(self.Class, type);
                list.Add(new GameMasteryTree(type, SkillData.PageName(self.Class, type), self.Mastery.InTree(type),
                    shown, skillWindow.CanSpendMastery(type), skillWindow.MasteryHint(type)));
            }
            return list;
        }
    }

    public IReadOnlyList<GameSkill> Skills(int category)
    {
        var list = new List<GameSkill>();
        foreach (var page in SkillData.Pages(self.Class, skillCast.SelfTransformModel(), SkillData.Get(self.TransformSkill), self.Nation))
        {
            if (page.Category != category) continue;
            foreach (var s in page.Skills)
            {
                bool met = self.SkillLearned(s);
                var icon = met ? SkillIcons.Get(s.Id) ?? SkillIcons.Enigma() : SkillIcons.Enigma();
                list.Add(new GameSkill(s.Id, s.Name, icon, met, SkillText.Tooltip(s)));
            }
        }
        return list;
    }

    public GameSkillInfo Info(int skillId)
    {
        if (SkillData.Get(skillId) is not { } s) return default;
        return new GameSkillInfo(s.Desc.Replace('|', '\n'), s.Msp, s.Level, s.Level, SkillData.MasteryType(s.Tree) > 0,
            SkillData.WeaponRequirementName(s.NeedWeapon),
            s.NeedItem != 0 ? ItemData.DisplayName(s.NeedItem) : "",
            s.ConsumedItem != 0 && s.ConsumedItem != s.NeedItem ? ItemData.DisplayName(s.ConsumedItem) : "");
    }

    public void SpendMastery(int type) => skillWindow.OnMasterySpend(type);

    public void AddToHotbar(int skillId) => hotbar.AddTo(skillId);
}
