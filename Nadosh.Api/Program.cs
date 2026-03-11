using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Nadosh.Infrastructure.Data;
using Nadosh.Core.Seed;

var builder = WebApplication.CreateBuilder(args);

// Epic 3 & 4: Error Handling & Rate Limiting
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var apiKey = context.Request.Headers["X-API-Key"].ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(apiKey, partition => new FixedWindowRateLimiterOptions
        {
            AutoReplenishment = true,
            PermitLimit = 100, // 100 requests per minute per API key
            QueueLimit = 0,
            Window = TimeSpan.FromMinutes(1)
        });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }
        await context.HttpContext.Response.WriteAsJsonAsync(new { Message = "Too many requests. Please try again later." }, token);
    };
});

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddNadoshInfrastructure(builder.Configuration);

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
var app = builder.Build();

// Seed data in dev environment
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NadoshDbContext>();
    
    // Ensure database is created and migrations are applied
    dbContext.Database.Migrate();

    if (!dbContext.RuleConfigs.Any())
    {
        dbContext.RuleConfigs.AddRange(DataSeeder.GenerateInitialRuleConfigs());
        dbContext.SaveChanges();
    }

    if (!dbContext.Targets.Any())
    {
        // Removed Bogus Internet scanning to avoid accidental external network sweeps.
        // Used local Demo approach via TargetsController instead.
        dbContext.SaveChanges();
    }
}

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseCors();

// Serve static files for frontend
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.UseHttpsRedirection();
app.MapHealthChecks("/health/ready");
app.Run();
