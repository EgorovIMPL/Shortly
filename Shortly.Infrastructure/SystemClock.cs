using Shortly.Application.Abstractions;

namespace Shortly.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}