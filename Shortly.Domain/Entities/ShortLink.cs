namespace Shortly.Domain.Entities;

public class ShortLink
{
    private ShortLink() { }

    public long Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string OriginalUrl { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long TotalClicks { get; private set; }

    public static ShortLink Create(
        string originalUrl,
        string code,
        DateTime nowUtc,
        DateTime? expiresAtUtc = null)
    {
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("Only absolute http/https URLs are supported.");
        }

        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Short code is required.");

        if (code.Length is < 4 or > 32)
            throw new DomainException("Short code length must be between 4 and 32 characters.");

        if (expiresAtUtc is not null && expiresAtUtc.Value <= nowUtc)
            throw new DomainException("Expiration date must be in the future.");

        return new ShortLink
        {
            Code = code,
            OriginalUrl = uri.ToString(),
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            IsActive = true,
            TotalClicks = 0
        };
    }

    public bool IsAvailable(DateTime nowUtc)
        => IsActive && (ExpiresAtUtc is null || ExpiresAtUtc.Value > nowUtc);

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainException("Link is already deactivated.");

        IsActive = false;
    }
}