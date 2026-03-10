using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nadosh.Core.Interfaces;
using Nadosh.Core.Scanning;
using StackExchange.Redis;

namespace Nadosh.Infrastructure.Data;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddNadoshInfrastructure(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
                               ?? "Server=localhost;Port=5432;Database=nadosh;User Id=nadosh;Password=nadosh_password;";
        
        services.AddDbContext<NadoshDbContext>(options =>
            options.UseNpgsql(connectionString, x => x.MigrationsAssembly("Nadosh.Infrastructure")));

        // Repositories
        services.AddScoped<ITargetRepository, TargetRepository>();
        services.AddScoped<IObservationRepository, ObservationRepository>();
        services.AddScoped<ICurrentExposureRepository, CurrentExposureRepository>();

        // Redis
        var redisConn = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(redisConn));

        // Queue (open generic — auto-creates queue per job type)
        services.AddSingleton(typeof(IJobQueue<>), typeof(Nadosh.Infrastructure.Queue.RedisJobQueue<>));

        // Scanning infrastructure
        services.AddSingleton<IScanRateLimiter, Nadosh.Infrastructure.Scanning.RedisScanRateLimiter>();
        services.AddSingleton<ILeaderElection, Nadosh.Infrastructure.Scanning.RedisLeaderElection>();
        services.AddSingleton<IPortSelectionStrategy, StaticPortSelectionStrategy>();
        services.AddSingleton<IServiceIdentifier, WellKnownServiceIdentifier>();
        services.AddSingleton<IMacVendorLookup, Nadosh.Infrastructure.Scanning.WiresharkMacVendorLookup>();
        services.AddSingleton<Nadosh.Infrastructure.Scanning.ArpScanner>();

        // Health Checks
        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "PostgreSQL")
            .AddRedis(redisConn, name: "Redis");

        return services;
    }
}
