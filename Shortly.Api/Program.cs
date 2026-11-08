using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Shortly.Api.Endpoints;
using Shortly.Api.Grpc;
using Shortly.Api.Hubs;
using Shortly.Application;
using Shortly.Domain;
using Shortly.Infrastructure;
using Shortly.Infrastructure.BackgroundJobs;
using Shortly.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IClickFlushNotifier, SignalRClickFlushNotifier>();
builder.Services.AddGrpc();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "Shortly API",
    Version = "v1",
    Description = "URL shortener: Clean Architecture, CQRS, two-level cache, batched click ingestion."
}));

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

await ApplyMigrationsAsync(app);

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    var (status, title, detail) = exception switch
    {
        ValidationException validation =>
            (StatusCodes.Status400BadRequest, "Validation failed",
             string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))),
        DomainException domain =>
            (StatusCodes.Status409Conflict, "Domain rule violated", domain.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "See server logs.")
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { title, detail, status });
}));

app.UseSerilogRequestLogging();
app.UseHttpMetrics();

app.UseSwagger();
app.UseSwaggerUI();

app.MapLinkEndpoints();
app.MapHub<StatsHub>("/hubs/stats");
app.MapGrpcService<LinkGrpcService>();
app.MapHealthChecks("/health");
app.MapMetrics();            // /metrics для Prometheus

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied.");
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning(ex, "Database is not ready (attempt {Attempt}/10), retrying...", attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    throw new InvalidOperationException("Could not apply migrations: database unavailable.");
}