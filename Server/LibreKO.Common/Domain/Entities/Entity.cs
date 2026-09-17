namespace LibreKO.Common.Domain.Entities;

public abstract class Entity
{
    public int Id { get; set; }
    public DateOnly CreatedAt { get; protected set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? UpdatedAt { get; set; }
}
