using MediatR;
using Shortly.Application.Abstractions;
using Shortly.Domain;

namespace Shortly.Application.Links;

public sealed record DeactivateLinkCommand(string Code) : IRequest;

public sealed class DeactivateLinkCommandHandler : IRequestHandler<DeactivateLinkCommand>
{
    private readonly ILinkRepository _repository;
    private readonly ICacheService _cache;

    public DeactivateLinkCommandHandler(ILinkRepository repository, ICacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task Handle(DeactivateLinkCommand request, CancellationToken ct)
    {
        var link = await _repository.GetByCodeAsync(request.Code, asNoTracking: false, ct)
                   ?? throw new DomainException($"Link '{request.Code}' not found.");

        link.Deactivate();
        await _repository.SaveChangesAsync(ct);

        // инвалидация кэша — иначе редирект продолжит работать до истечения TTL
        await _cache.RemoveAsync(ResolveLinkQueryHandler.CacheKey(request.Code), ct);
    }
}