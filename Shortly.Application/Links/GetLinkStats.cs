using MediatR;
using Shortly.Application.Abstractions;
using Shortly.Domain;

namespace Shortly.Application.Links;

public sealed record GetLinkStatsQuery(
    string Code,
    DateTime? FromUtc,
    DateTime? ToUtc) : IRequest<LinkStatsDto>;

public sealed class GetLinkStatsQueryHandler : IRequestHandler<GetLinkStatsQuery, LinkStatsDto>
{
    private readonly ILinkRepository _repository;
    private readonly IClickStatsReader _statsReader;
    private readonly IClock _clock;

    public GetLinkStatsQueryHandler(
        ILinkRepository repository,
        IClickStatsReader statsReader,
        IClock clock)
    {
        _repository = repository;
        _statsReader = statsReader;
        _clock = clock;
    }

    public async Task<LinkStatsDto> Handle(GetLinkStatsQuery request, CancellationToken ct)
    {
        var link = await _repository.GetByCodeAsync(request.Code, asNoTracking: true, ct)
                   ?? throw new DomainException($"Link '{request.Code}' not found.");

        var toUtc = request.ToUtc ?? _clock.UtcNow;
        var fromUtc = request.FromUtc ?? toUtc.AddDays(-7);

        if (fromUtc >= toUtc)
            throw new DomainException("'from' must be earlier than 'to'.");

        // агрегаты читаются Dapper'ом: сырой SQL быстрее и прозрачнее по плану запроса
        var hourly = await _statsReader.GetHourlyAsync(link.Id, fromUtc, toUtc, ct);
        var referers = await _statsReader.GetTopReferersAsync(link.Id, fromUtc, toUtc, 10, ct);

        return new LinkStatsDto(link.Code, link.TotalClicks, hourly, referers);
    }
}