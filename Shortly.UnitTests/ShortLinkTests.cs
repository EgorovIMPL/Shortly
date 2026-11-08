using FluentAssertions;
using Shortly.Domain;
using Shortly.Domain.Entities;
using Xunit;

namespace Shortly.UnitTests;

public class ShortLinkTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    public void Create_RejectsNonHttpUrls(string url)
    {
        var act = () => ShortLink.Create(url, "abcd", Now);

        act.Should().Throw<DomainException>()
            .WithMessage("*http/https*");
    }

    [Fact]
    public void Create_RejectsExpirationInThePast()
    {
        var act = () => ShortLink.Create("https://example.com", "abcd", Now, Now.AddMinutes(-1));

        act.Should().Throw<DomainException>()
            .WithMessage("*future*");
    }

    [Fact]
    public void Create_SetsActiveLinkWithZeroClicks()
    {
        var link = ShortLink.Create("https://example.com/page", "abcd", Now);

        link.IsActive.Should().BeTrue();
        link.TotalClicks.Should().Be(0);
        link.IsAvailable(Now).Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_ReturnsFalseAfterExpiration()
    {
        var link = ShortLink.Create("https://example.com", "abcd", Now, Now.AddHours(1));

        link.IsAvailable(Now.AddMinutes(59)).Should().BeTrue();
        link.IsAvailable(Now.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void Deactivate_IsNotIdempotent()
    {
        var link = ShortLink.Create("https://example.com", "abcd", Now);
        link.Deactivate();

        link.IsAvailable(Now).Should().BeFalse();
        link.Invoking(l => l.Deactivate()).Should().Throw<DomainException>();
    }

    [Fact]
    public void ShortCodeGenerator_ProducesUniqueCodes()
    {
        var codes = Enumerable.Range(0, 1_000).Select(_ => ShortCodeGenerator.Next()).ToList();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length == 7);
    }
}