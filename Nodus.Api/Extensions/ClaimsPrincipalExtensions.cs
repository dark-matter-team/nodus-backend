using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Nodus.Api.Exceptions;

namespace Nodus.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedException("Invalid token");

        return userId;
    }
}