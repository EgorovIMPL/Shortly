using System.Security.Cryptography;
using System.Text;
using MediatR;
using Shortly.Application.Abstractions;
using Shortly.Application.Links;
using Shortly.Domain.Entities;

namespace Shortly.Api.Endpoints;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/links").WithTags("Links");

        api.MapPost("/", async (CreateLinkRequest request, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(
                new CreateLinkCommand(request.OriginalUrl, request.CustomCode, request.ExpiresAtUtc), ct);

            return Results.Created($"/api/links/{dto.Code}", dto);
        })
        .WithSummary("Создать короткую ссылку");

        api.MapGet("/{code}/stats", async (
            string code,
            DateTime? from,
            DateTime? to,
            ISender sender,
            CancellationToken ct) =>
        {
            var stats = await sender.Send(new GetLinkStatsQuery(code, from, to), ct);
            return Results.Ok(stats);
        })
        .WithSummary("Статистика переходов по часам");

        api.MapDelete("/{code}", async (string code, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeactivateLinkCommand(code), ct);
            return Results.NoContent();
        })
        .WithSummary("Деактивировать ссылку");

        // Горячий путь: только кэш + постановка клика в очередь, без обращения к БД
        app.MapGet("/{code:minlength(4):maxlength(32)}", async (
            string code,
            HttpContext http,
            ISender sender,
            IClickQueue queue,
            IClock clock,
            CancellationToken ct) =>
        {
            var link = await sender.Send(new ResolveLinkQuery(code), ct);
            if (link is null)
                return Results.NotFound(new { error = $"Link '{code}' not found." });

            queue.TryEnqueue(ClickEvent.Create(
                link.LinkId,
                clock.UtcNow,
                NormalizeReferer(http.Request.Headers.Referer.ToString()),
                http.Request.Headers.UserAgent.ToString(),
                HashIp(http.Connection.RemoteIpAddress?.ToString())));

            return Results.Redirect(link.OriginalUrl, permanent: false, preserveMethod: false);
        })
        .WithTags("Redirect")
        .ExcludeFromDescription();
    }

    private static string? NormalizeReferer(string? referer)
        => Uri.TryCreate(referer, UriKind.Absolute, out var uri) ? uri.Host : null;

    /// <summary>IP не хранится в открытом виде — только соль+хэш для подсчёта уникальных.</summary>
    private static string? HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"shortly::{ip}"));
        return Convert.ToHexString(bytes)[..32];
    }

    public sealed record CreateLinkRequest(
        string OriginalUrl,
        string? CustomCode,
        DateTime? ExpiresAtUtc);
}