using MediatR;
using Shortly.Application.Abstractions;

namespace Shortly.Application.Links;

public sealed record ResolveLinkQuery(string Code) : IRequest<ResolvedLinkDto?>;

public sealed class ResolveLinkQueryHandler : IRequestHandler<ResolveLinkQuery, ResolvedLinkDto?>
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(1);
    private const string MissMarker = "-";

    private readonly ILinkRepository _repository;
    private readonly ICacheService _cache;
    private readonly IClock _clock;

    public ResolveLinkQueryHandler(ILinkRepository repository, ICacheService cache, IClock clock)
    {
        _repository = repository;
        _cache = cache;
        _clock = clock;
    }

    public async Task<ResolvedLinkDto?> Handle(ResolveLinkQuery request, CancellationToken ct)
    {
        var key = CacheKey(request.Code);

        var cached = await _cache.GetAsync(key, ct);
        if (cached is not null)
            return Deserialize(cached);

        // asNoTracking: запрос только читает, трекинг EF здесь только тратит память
        var link = await _repository.GetByCodeAsync(request.Code, asNoTracking: true, ct);

        if (link is null || !link.IsAvailable(_clock.UtcNow))
        {
            // negative caching: защищает БД от перебора несуществующих кодов
            await _cache.SetAsync(key, MissMarker, MissTtl, ct);
            return null;
        }

        var result = new ResolvedLinkDto(link.Id, link.OriginalUrl);
        await _cache.SetAsync(key, Serialize(result), HitTtl, ct);
        return result;
    }

    public static string CacheKey(string code) => $"link:{code}";

    private static string Serialize(ResolvedLinkDto dto) => $"{dto.LinkId}|{dto.OriginalUrl}";

    private static ResolvedLinkDto? Deserialize(string raw)
    {
        if (raw == MissMarker) return null;

        var separator = raw.IndexOf('|');
        if (separator <= 0) return null;

        return long.TryParse(raw[..separator], out var id)
            ? new ResolvedLinkDto(id, raw[(separator + 1)..])
            : null;
    }
}