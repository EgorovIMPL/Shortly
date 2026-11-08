using Dapper;
using Npgsql;
using Shortly.Application.Abstractions;
using Shortly.Application.Links;

namespace Shortly.Infrastructure.Persistence;

public sealed class ClickStatsReader : IClickStatsReader
{
    private readonly string _connectionString;

    public ClickStatsReader(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<ClickBucketDto>> GetHourlyAsync(
        long linkId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        const string sql = """
            SELECT date_trunc('hour', occurred_at_utc) AS "BucketUtc",
                   COUNT(*)                            AS "Clicks"
            FROM click_events
            WHERE link_id = @LinkId
              AND occurred_at_utc >= @FromUtc
              AND occurred_at_utc <  @ToUtc
            GROUP BY 1
            ORDER BY 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(sql,
            new { LinkId = linkId, FromUtc = fromUtc, ToUtc = toUtc },
            cancellationToken: ct);

        var rows = await connection.QueryAsync<ClickBucketDto>(command);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RefererStatDto>> GetTopReferersAsync(
        long linkId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(referer, 'direct') AS "Referer",
                   COUNT(*)                    AS "Clicks"
            FROM click_events
            WHERE link_id = @LinkId
              AND occurred_at_utc >= @FromUtc
              AND occurred_at_utc <  @ToUtc
            GROUP BY 1
            ORDER BY 2 DESC
            LIMIT @Limit;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(sql,
            new { LinkId = linkId, FromUtc = fromUtc, ToUtc = toUtc, Limit = limit },
            cancellationToken: ct);

        var rows = await connection.QueryAsync<RefererStatDto>(command);
        return rows.ToList();
    }
}