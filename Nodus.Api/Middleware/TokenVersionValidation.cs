using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Nodus.Api.Data;
using Nodus.Api.Extensions;

namespace Nodus.Api.Middleware;

/// <summary>
/// Stateless JWTs cannot be revoked by themselves. Every token carries the user's TokenVersion;
/// resetting the password increments it, and deleting the account removes the user,
/// so older tokens stop working immediately instead of living for the whole 7-day TTL.
/// </summary>
public static class TokenVersionValidation
{
    public const string ClaimType = "token_version";

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        if (context.Principal is null)
        {
            context.Fail("Invalid token");
            return;
        }

        Guid userId;
        try
        {
            userId = context.Principal.GetUserId();
        }
        catch (Exceptions.UnauthorizedException)
        {
            context.Fail("Invalid token");
            return;
        }

        // Tokens issued before this check existed have no claim and match the initial version 0.
        var tokenVersion = int.TryParse(context.Principal.FindFirst(ClaimType)?.Value, out var version) ? version : 0;

        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var isCurrent = await dbContext.Users
            .AnyAsync(u => u.Id == userId && u.TokenVersion == tokenVersion, context.HttpContext.RequestAborted);

        if (!isCurrent)
            context.Fail("Token has been revoked");
    }
}
