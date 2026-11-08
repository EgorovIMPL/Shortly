using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using Shortly.Infrastructure.Queue;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Shortly.Infrastructure.BackgroundJobs;

/// <summary>Читает канал кликов и сбрасывает их в БД батчами: по размеру или по таймауту.</summary>
public sealed class ClickFlushService : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private static readonly Histogram FlushDuration = Metrics.CreateHistogram(
        "shortly_click_flush_duration_seconds", "Duration of a click batch flush.");

    private static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "shortly_click_queue_depth", "Current number of buffered clicks.");

    private readonly ChannelClickQueue _queue;
    private readonly ClickBatchWriter _writer;
    private readonly IClickFlushNotifier _notifier;
    private readonly ILogger<ClickFlushService> _logger;

    public ClickFlushService(
        ChannelClickQueue queue,
        ClickBatchWriter writer,
        IClickFlushNotifier notifier,
        ILogger<ClickFlushService> logger)
    {
        _queue = queue;
        _writer = writer;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Click flush service started (batch={BatchSize}).", BatchSize);

        var batch = new List<Domain.Entities.ClickEvent>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FillBatchAsync(batch, stoppingToken);

                if (batch.Count == 0) continue;

                var stopwatch = Stopwatch.StartNew();
                await _writer.WriteAsync(batch, stoppingToken);
                FlushDuration.Observe(stopwatch.Elapsed.TotalSeconds);

                _queue.OnDequeued(batch.Count);
                QueueDepth.Set(_queue.Depth);

                var affected = batch.Select(c => c.LinkId).Distinct().ToArray();
                await _notifier.NotifyAsync(affected, batch.Count, stoppingToken);

                _logger.LogDebug("Flushed {Count} clicks for {Links} links in {Elapsed} ms.",
                    batch.Count, affected.Length, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // батч потерян, но воркер не падает — иначе остановится вся статистика
                _logger.LogError(ex, "Failed to flush {Count} clicks.", batch.Count);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                batch.Clear();
            }
        }

        _logger.LogInformation("Click flush service stopped.");
    }

    private async Task FillBatchAsync(
        List<Domain.Entities.ClickEvent> batch, CancellationToken stoppingToken)
    {
        if (!await _queue.Reader.WaitToReadAsync(stoppingToken))
            return;

        using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        window.CancelAfter(FlushInterval);

        try
        {
            while (batch.Count < BatchSize)
            {
                if (_queue.Reader.TryRead(out var click))
                {
                    batch.Add(click);
                    continue;
                }

                if (!await _queue.Reader.WaitToReadAsync(window.Token))
                    break;
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // истекло окно ожидания — отправляем то, что успели накопить
        }
    }
}

/// <summary>Абстракция над SignalR, чтобы инфраструктура не зависела от хоста.</summary>
public interface IClickFlushNotifier
{
    Task NotifyAsync(IReadOnlyCollection<long> linkIds, int clicks, CancellationToken ct);
}