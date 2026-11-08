namespace Shortly.Application.Links;

public sealed record LinkDto(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    long TotalClicks);

public sealed record ResolvedLinkDto(long LinkId, string OriginalUrl);

public sealed record ClickBucketDto(DateTime BucketUtc, long Clicks);

public sealed record RefererStatDto(string Referer, long Clicks);

public sealed record LinkStatsDto(
    string Code,
    long TotalClicks,
    IReadOnlyList<ClickBucketDto> Hourly,
    IReadOnlyList<RefererStatDto> TopReferers);