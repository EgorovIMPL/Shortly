using Shortly.Domain.Entities;

namespace Shortly.Application.Abstractions;

public interface IClickQueue
{
    /// <summary>Неблокирующая постановка в очередь. false — очередь переполнена, клик отброшен.</summary>
    bool TryEnqueue(ClickEvent click);

    int Depth { get; }
}