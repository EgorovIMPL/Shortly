using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Shortly.Application.Abstractions;
using Shortly.Domain;
using Shortly.Domain.Entities;

namespace Shortly.Application.Links;

public sealed record CreateLinkCommand(
    string OriginalUrl,
    string? CustomCode,
    DateTime? ExpiresAtUtc) : IRequest<LinkDto>;

public sealed class CreateLinkCommandValidator : AbstractValidator<CreateLinkCommand>
{
    public CreateLinkCommandValidator()
    {
        RuleFor(x => x.OriginalUrl)
            .NotEmpty()
            .MaximumLength(2048);

        RuleFor(x => x.CustomCode)
            .Matches("^[A-Za-z0-9_-]{4,32}$")
            .When(x => !string.IsNullOrWhiteSpace(x.CustomCode))
            .WithMessage("Custom code may contain only letters, digits, '-' and '_' (4..32 chars).");
    }
}

public sealed class CreateLinkCommandHandler : IRequestHandler<CreateLinkCommand, LinkDto>
{
    private const int MaxCodeAttempts = 5;

    private readonly ILinkRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger<CreateLinkCommandHandler> _logger;

    public CreateLinkCommandHandler(
        ILinkRepository repository,
        IClock clock,
        ILogger<CreateLinkCommandHandler> logger)
    {
        _repository = repository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LinkDto> Handle(CreateLinkCommand request, CancellationToken ct)
    {
        var code = await ResolveCodeAsync(request.CustomCode, ct);

        var link = ShortLink.Create(
            request.OriginalUrl,
            code,
            _clock.UtcNow,
            request.ExpiresAtUtc);

        await _repository.AddAsync(link, ct);
        await _repository.SaveChangesAsync(ct);

        _logger.LogInformation("Short link created: {Code} -> {Url}", link.Code, link.OriginalUrl);

        return link.ToDto();
    }

    private async Task<string> ResolveCodeAsync(string? customCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(customCode))
        {
            if (await _repository.CodeExistsAsync(customCode, ct))
                throw new DomainException($"Code '{customCode}' is already taken.");

            return customCode;
        }

        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var generated = ShortCodeGenerator.Next();
            if (!await _repository.CodeExistsAsync(generated, ct))
                return generated;
        }

        throw new DomainException("Unable to generate a unique short code, please retry.");
    }
}

internal static class LinkMappings
{
    public static LinkDto ToDto(this ShortLink link) => new(
        link.Code,
        $"/{link.Code}",
        link.OriginalUrl,
        link.CreatedAtUtc,
        link.ExpiresAtUtc,
        link.TotalClicks);
}