using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IPatchRepository
{
    Task<List<Patch>> GetPatchList();
}
