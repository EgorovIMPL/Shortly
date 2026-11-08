using Microsoft.EntityFrameworkCore;
using Shortly.Application.Abstractions;
using Shortly.Domain.Entities;

namespace Shortly.Infrastructure.Persistence;

public sealed class LinkRepository : ILinkRepository
{
    private readonly AppDbContext _db;

    public LinkRepository(AppDbContext db) => _db = db;

    public Task<ShortLink?> GetByCodeAsync(string code, bool asNoTracking, CancellationToken ct)
    {
        var query = _db.Links.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(x => x.Code == code, ct);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct)
        => _db.Links.AsNoTracking().AnyAsync(x => x.Code == code, ct);

    public async Task AddAsync(ShortLink link, CancellationToken ct)
        => await _db.Links.AddAsync(link, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}