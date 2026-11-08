using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortly.Application.Abstractions;
using Shortly.Infrastructure.BackgroundJobs;
using Shortly.Infrastructure.Caching;
using Shortly.Infrastructure.Persistence;
using Shortly.Infrastructure.Queue;
using StackExchange.Redis;

namespace Shortly.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
                       ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(postgres)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ILinkRepository, LinkRepository>();
        services.AddScoped<IClickStatsReader>(_ => new ClickStatsReader(postgres));
        services.AddSingleton(_ => new ClickBatchWriter(postgres));

        services.AddMemoryCache();
        services.AddSingleton<ICacheService, TwoLevelCache>();
        services.AddSingleton<IClock, SystemClock>();

        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var options = ConfigurationOptions.Parse(redis);
                options.AbortOnConnectFail = false;   // стартуем даже если Redis ещё не поднялся
                options.ConnectRetry = 3;

                var logger = sp.GetRequiredService<ILogger<TwoLevelCache>>();
                var multiplexer = ConnectionMultiplexer.Connect(options);

                multiplexer.ConnectionFailed += (_, e) =>
                    logger.LogWarning("Redis connection failed: {Endpoint}", e.EndPoint);

                return multiplexer;
            });
        }

        services.AddSingleton<ChannelClickQueue>();
        services.AddSingleton<IClickQueue>(sp => sp.GetRequiredService<ChannelClickQueue>());
        services.AddHostedService<ClickFlushService>();

        return services;
    }
}