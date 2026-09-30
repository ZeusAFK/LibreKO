using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IKnightsRepository
{
    Task<bool> IsNameTakenAsync(string name);
    Task CreateAsync(KnightsEntity clan);
    Task<KnightsEntity?> FindAsync(short id);
    Task RemoveAsync(KnightsEntity entity);
    Task UpdateAsync(KnightsEntity clan);
    Task<List<ClanMemberProjection>> GetMembersAsync(short knightsId);
    Task<List<Character>> GetCharactersByClanAsync(short knightsId);
    Task<List<ClanMemberLoyalty>> GetMemberLoyaltyAsync();
    Task SyncCharacterClanStateAsync(int characterId, short knightsId, byte fame, int? money = null, int? loyalty = null);
}

public readonly record struct ClanMemberLoyalty(short ClanId, string Name, int Loyalty);

public class ClanMemberProjection
{
    public required string Name { get; init; }
    public byte Fame { get; init; }
    public byte Level { get; init; }
    public short Class { get; init; }
    public int Loyalty { get; init; }
    public int DonatedPoints { get; init; }
    public DateTime? LastOnlineTime { get; init; }
}
