using Shortly.Application.Links;

namespace Shortly.Application.Abstractions;

public interface IClickStatsReader
{
    Task<IReadOnlyList<ClickBucketDto>> GetHourlyAsync(
        long linkId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    Task<IReadOnlyList<RefererStatDto>> GetTopReferersAsync(
        long linkId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct);
}