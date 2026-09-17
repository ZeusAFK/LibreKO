using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Persistence.Seed;

namespace LibreKO.Login.Seed;

public class ServerSeed : JsonEntitySeed<Server>
{
    protected override string JsonFileName => "Servers.json";
    public override bool PerformUpdate => false;
}
