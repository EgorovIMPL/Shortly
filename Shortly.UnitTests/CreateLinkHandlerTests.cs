using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shortly.Application.Abstractions;
using Shortly.Application.Links;
using Shortly.Domain;
using Shortly.Domain.Entities;
using Xunit;

namespace Shortly.UnitTests;

public class CreateLinkHandlerTests
{
    private readonly ILinkRepository _repository = Substitute.For<ILinkRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly CreateLinkCommandHandler _handler;

    public CreateLinkHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc));
        _handler = new CreateLinkCommandHandler(
            _repository, _clock, NullLogger<CreateLinkCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_GeneratesCode_WhenCustomCodeIsNotProvided()
    {
        _repository.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(
            new CreateLinkCommand("https://example.com/a", null, null), CancellationToken.None);

        result.Code.Should().HaveLength(7);
        result.ShortUrl.Should().Be($"/{result.Code}");
        await _repository.Received(1).AddAsync(Arg.Any<ShortLink>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Throws_WhenCustomCodeIsTaken()
    {
        _repository.CodeExistsAsync("promo", Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.Handle(
            new CreateLinkCommand("https://example.com", "promo", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already taken*");
        await _repository.DidNotReceive().AddAsync(Arg.Any<ShortLink>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RetriesCodeGeneration_OnCollision()
    {
        var calls = 0;
        _repository.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls <= 2);   // первые две попытки заняты

        var result = await _handler.Handle(
            new CreateLinkCommand("https://example.com", null, null), CancellationToken.None);

        result.Should().NotBeNull();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Handle_Throws_AfterTooManyCollisions()
    {
        _repository.CodeExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _handler.Handle(
            new CreateLinkCommand("https://example.com", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*unique short code*");
    }
}