using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Prometheus;
using Shortly.Application.Abstractions;
using StackExchange.Redis;
using System.Diagnostics.Metrics;

namespace Shortly.Infrastructure.Caching;

/// <summary>
/// L1 — IMemoryCache (процесс), L2 — Redis (общий для всех инстансов).
/// Redis недоступен — сервис продолжает работать на L1 и БД.
/// </summary>
public sealed class TwoLevelCache : ICacheService
{
    private static readonly Counter Hits = Metrics
        .CreateCounter("shortly_cache_hits_total", "Cache hits.", "level");

    private static readonly Counter Misses = Metrics
        .CreateCounter("shortly_cache_misses_total", "Cache misses.");

    private static readonly TimeSpan L1MaxTtl = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _memory;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<TwoLevelCache> _logger;

    public TwoLevelCache(
        IMemoryCache memory,
        ILogger<TwoLevelCache> logger,
        IConnectionMultiplexer? redis = null)
    {
        _memory = memory;
        _logger = logger;
        _redis = redis;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        if (_memory.TryGetValue<string>(key, out var local) && local is not null)
        {
            Hits.WithLabels("l1").Inc();
            return local;
        }

        var db = TryGetDatabase();
        if (db is not null)
        {
            try
            {
                var value = await db.StringGetAsync(key);
                if (value.HasValue)
                {
                    Hits.WithLabels("l2").Inc();
                    _memory.Set(key, value.ToString(), L1MaxTtl);
                    return value.ToString();
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis GET failed for {Key}, falling back to database.", key);
            }
        }

        Misses.Inc();
        return null;
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        _memory.Set(key, value, ttl < L1MaxTtl ? ttl : L1MaxTtl);

        var db = TryGetDatabase();
        if (db is null) return;

        try
        {
            await db.StringSetAsync(key, value, ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for {Key}.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        _memory.Remove(key);

        var db = TryGetDatabase();
        if (db is null) return;

        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for {Key}.", key);
        }
    }

    private IDatabase? TryGetDatabase()
        => _redis is { IsConnected: true } ? _redis.GetDatabase() : null;
}