using Prometheus;
using Shortly.Application.Abstractions;
using Shortly.Domain.Entities;
using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace Shortly.Infrastructure.Queue;

/// <summary>
/// Буфер кликов в памяти. Редирект не пишет в БД — он только кладёт событие в канал,
/// поэтому латентность редиректа не зависит от БД.
/// </summary>
public sealed class ChannelClickQueue : IClickQueue
{
    private static readonly Counter Dropped = Metrics
        .CreateCounter("shortly_clicks_dropped_total", "Clicks dropped because the queue was full.");

    private static readonly Counter Enqueued = Metrics
        .CreateCounter("shortly_clicks_enqueued_total", "Clicks accepted into the queue.");

    private readonly Channel<ClickEvent> _channel;
    private int _depth;

    public ChannelClickQueue(int capacity = 50_000)
    {
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(capacity)
        {
            // статистика не критичнее доступности редиректа: при переполнении теряем клик, а не ждём
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public int Depth => Volatile.Read(ref _depth);

    public bool TryEnqueue(ClickEvent click)
    {
        if (_channel.Writer.TryWrite(click))
        {
            Interlocked.Increment(ref _depth);
            Enqueued.Inc();
            return true;
        }

        Dropped.Inc();
        return false;
    }

    internal void OnDequeued(int count) => Interlocked.Add(ref _depth, -count);
}