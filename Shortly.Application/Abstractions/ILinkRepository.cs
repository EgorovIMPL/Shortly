using Shortly.Domain.Entities;

namespace Shortly.Application.Abstractions;

public interface ILinkRepository
{
    Task<ShortLink?> GetByCodeAsync(string code, bool asNoTracking, CancellationToken ct);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct);
    Task AddAsync(ShortLink link, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}