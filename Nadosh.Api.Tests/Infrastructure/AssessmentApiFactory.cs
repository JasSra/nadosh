using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nadosh.Infrastructure.Data;

namespace Nadosh.Api.Tests.Infrastructure;

public sealed class AssessmentApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-api-key";
    private readonly string _databaseName = $"nadosh-api-tests-{Guid.NewGuid():N}";
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSettings:ApiKey"] = ApiKey,
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=nadosh;Username=nadosh;Password=nadosh",
                ["Redis:ConnectionString"] = "localhost:6379,abortConnect=false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<NadoshDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<NadoshDbContext>>();
            services.RemoveAll<NadoshDbContext>();

            services.AddDbContext<NadoshDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName, _databaseRoot));
        });
    }

    public async Task SeedAsync(Func<NadoshDbContext, Task> seedAsync)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await seedAsync(dbContext);
    }
}
