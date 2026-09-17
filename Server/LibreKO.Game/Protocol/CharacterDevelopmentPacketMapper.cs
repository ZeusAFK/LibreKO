using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal static class CharacterDevelopmentPacketMapper
{

    public static Packet CreateStatResetSuccess(UserSession session) =>
        ClassChangePacketWriter.StatResetSuccess(new ClassChangePacketWriter.StatBlock(
            session.Money,
            session.Strength,
            session.Stamina,
            session.Dexterity,
            session.Intelligence,
            session.Magic,
            session.MaxHp,
            session.MaxMp,
            (short)session.Stats.TotalHit,
            session.Stats.MaxWeight,
            session.StatPoints));

    public static Packet CreateStatResetFailure(ResetResult reason, int cost) =>
        ClassChangePacketWriter.Failure(ClassChangeSubOpcode.StatReset, reason, cost);

    public static Packet CreateSkillResetSuccess(UserSession session) =>
        ClassChangePacketWriter.SkillResetSuccess(
            session.Money, session.SkillPoints[ProgressionTable.MasteryPoolSlot]);

    public static Packet CreateSkillResetFailure(ResetResult reason, int cost) =>
        ClassChangePacketWriter.Failure(ClassChangeSubOpcode.SkillReset, reason, cost);
}
