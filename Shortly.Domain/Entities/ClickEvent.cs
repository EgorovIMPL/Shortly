namespace Shortly.Domain.Entities;

public class ClickEvent
{
    private ClickEvent() { }

    public long Id { get; private set; }
    public long LinkId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? Referer { get; private set; }
    public string? UserAgent { get; private set; }
    public string? IpHash { get; private set; }

    public static ClickEvent Create(
        long linkId,
        DateTime occurredAtUtc,
        string? referer,
        string? userAgent,
        string? ipHash)
    {
        if (linkId <= 0) throw new DomainException("LinkId must be positive.");

        return new ClickEvent
        {
            LinkId = linkId,
            OccurredAtUtc = occurredAtUtc,
            Referer = Truncate(referer, 512),
            UserAgent = Truncate(userAgent, 512),
            IpHash = Truncate(ipHash, 64)
        };
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}