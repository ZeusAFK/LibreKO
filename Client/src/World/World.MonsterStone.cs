using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private Notice? _nestConfirm;

    private void MonsterStoneInit() => Net.I.MonsterStoneEvent += OnMonsterStoneResult;

    private void MonsterStoneDispose()
    {
        Net.I.MonsterStoneEvent -= OnMonsterStoneResult;
        DismissNestConfirm();
    }

    private static bool OpensNestDungeon(SkillData.Skill? skill) =>
        skill?.CooldownGroup == NestDungeon.EventSummonGroup;

    private void OpenNestDungeon(int itemId, SkillData.Skill skill)
    {
        string name = ItemData.DisplayName(itemId);
        if (InParty)
        {
            CombatNotice(NestDungeon.InPartyRefusal);
            return;
        }
        if (_selfDead)
        {
            CombatNotice(NestDungeon.DeadRefusal(name));
            return;
        }

        DismissNestConfirm();
        string explain = ItemData.Get(itemId)?.Desc is { Length: > 0 } desc ? desc : skill.Name;
        _nestConfirm = Notice.Confirm(this, explain, Localization.Loc.Tr("Enter"), Localization.Loc.Tr("Cancel"),
            () => ConfirmNestDungeon(itemId), () => _nestConfirm = null, name);
    }

    private void ConfirmNestDungeon(int itemId)
    {
        _nestConfirm = null;
        if (_selfDead)
        {
            CombatNotice(NestDungeon.DeadRefusal(ItemData.DisplayName(itemId)));
            return;
        }
        Net.I.SendMonsterStone(itemId);
    }

    private void OnMonsterStoneResult(MonsterStoneResult result, int itemId)
    {
        if (NestDungeon.Message(result, _zone) is { } message)
            CombatNotice(message);
    }

    private void DismissNestConfirm()
    {
        if (_nestConfirm != null && GodotObject.IsInstanceValid(_nestConfirm))
            _nestConfirm.Close();
        _nestConfirm = null;
    }
}
