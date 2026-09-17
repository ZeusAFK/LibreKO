using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Persistence.Seed;

namespace LibreKO.Login.Seed;

public class ServerGroupSeed : JsonEntitySeed<ServerGroup>
{
    protected override string JsonFileName => "ServerGroups.json";
    public override bool PerformUpdate => false;
}
