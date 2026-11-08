namespace Shortly.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}