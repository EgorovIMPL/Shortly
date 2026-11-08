using Npgsql;
using NpgsqlTypes;
using Shortly.Domain.Entities;

namespace Shortly.Infrastructure.BackgroundJobs;

/// <summary>
/// Батчевая запись кликов. Вместо N insert'ов — один запрос с массивами (UNNEST)
/// и один UPDATE денормализованного счётчика.
/// </summary>
public sealed class ClickBatchWriter
{
    private const string InsertSql = """
        INSERT INTO click_events (link_id, occurred_at_utc, referer, user_agent, ip_hash)
        SELECT * FROM UNNEST(@link_ids, @occurred_at, @referers, @user_agents, @ip_hashes);
        """;

    private const string UpdateCountersSql = """
        UPDATE short_links sl
        SET total_clicks = sl.total_clicks + agg.cnt
        FROM (
            SELECT link_id, COUNT(*) AS cnt
            FROM UNNEST(@link_ids) AS link_id
            GROUP BY link_id
        ) AS agg
        WHERE sl.id = agg.link_id;
        """;

    private readonly string _connectionString;

    public ClickBatchWriter(string connectionString) => _connectionString = connectionString;

    public async Task WriteAsync(IReadOnlyList<ClickEvent> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        var linkIds = new long[batch.Count];
        var occurredAt = new DateTime[batch.Count];
        var referers = new string?[batch.Count];
        var userAgents = new string?[batch.Count];
        var ipHashes = new string?[batch.Count];

        for (var i = 0; i < batch.Count; i++)
        {
            linkIds[i] = batch[i].LinkId;
            occurredAt[i] = batch[i].OccurredAtUtc;
            referers[i] = batch[i].Referer;
            userAgents[i] = batch[i].UserAgent;
            ipHashes[i] = batch[i].IpHash;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var insert = new NpgsqlCommand(InsertSql, connection, transaction))
        {
            insert.Parameters.Add(ArrayParam("link_ids", NpgsqlDbType.Bigint, linkIds));
            insert.Parameters.Add(ArrayParam("occurred_at", NpgsqlDbType.TimestampTz, occurredAt));
            insert.Parameters.Add(ArrayParam("referers", NpgsqlDbType.Text, referers));
            insert.Parameters.Add(ArrayParam("user_agents", NpgsqlDbType.Text, userAgents));
            insert.Parameters.Add(ArrayParam("ip_hashes", NpgsqlDbType.Text, ipHashes));

            await insert.ExecuteNonQueryAsync(ct);
        }

        await using (var update = new NpgsqlCommand(UpdateCountersSql, connection, transaction))
        {
            update.Parameters.Add(ArrayParam("link_ids", NpgsqlDbType.Bigint, linkIds));
            await update.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static NpgsqlParameter ArrayParam<T>(string name, NpgsqlDbType elementType, T[] values)
        => new(name, NpgsqlDbType.Array | elementType) { Value = values };
}