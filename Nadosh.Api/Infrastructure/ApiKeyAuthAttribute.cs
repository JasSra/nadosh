using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Nadosh.Api.Infrastructure;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { Message = "API Key is missing." });
            return;
        }

        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredApiKey = configuration.GetValue<string>("ApiSettings:ApiKey");

        if (string.IsNullOrEmpty(configuredApiKey) || !configuredApiKey.Equals(extractedApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { Message = "Invalid API Key." });
            return;
        }

        await next();
    }
}
