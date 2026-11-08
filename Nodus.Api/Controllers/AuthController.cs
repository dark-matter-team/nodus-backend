using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Nodus.Api.Controllers;

[ApiController]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("/api/auth/google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        try
        {
            return Ok(await authService.GoogleLoginAsync(request.IdToken));
        }
        catch(AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });

        }
    }

    [HttpPost("/api/auth/register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            return Ok(await authService.RegisterAsync(request.Email, request.Password, request.Nickname));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("/api/auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            return Ok(await authService.LoginAsync(request.Email, request.Password, request.Nickname));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("/api/users/me")]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser()
    {
        try
        {
            return Ok(await authService.GetUserProfileAsync(GetUserId()));
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpDelete("/api/users/me")]
    public async Task<IActionResult> DeleteAccount()
    {
        try
        {
            var deleted = await authService.DeleteUserAccountAsync(GetUserId());
            return deleted ? NoContent() : NotFound(new { error = "Користувача не знайдено" });
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    private Guid GetUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idClaim, out var userId))
            throw new AuthException("Недійсний токен", StatusCodes.Status401Unauthorized);

        return userId;
    }
}