using System.Threading.RateLimiting;
using Nodus.Api.Extensions;

namespace Nodus.Api.Middleware;

public static class RateLimitPolicies
{
    public const string Generate = "generate";

    /// <summary>
    /// Limits AI generations per user (fixed window). Settings: "RateLimits:Generate:PermitLimit" and ":WindowMinutes".
    /// </summary>
    public static IServiceCollection AddGenerateRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimits:Generate:PermitLimit", 5);
        var window = TimeSpan.FromMinutes(configuration.GetValue("RateLimits:Generate:WindowMinutes", 10));

        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Generate, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.Identity?.IsAuthenticated == true
                    ? context.User.GetUserId().ToString()
                    : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "Too many roadmap generations. Please try again later." }, cancellationToken);
            };
        });
    }
}
