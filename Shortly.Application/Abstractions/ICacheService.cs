namespace Shortly.Application.Abstractions;

public interface ICacheService
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}