using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
public class AuthController(AuthService authService) : ControllerBase
{
    private static UserProfileDto CreateMockUser(string email = "student@knu.ua", string nickname = "NodusUser") =>
        new(Guid.NewGuid(), email, nickname, "https://api.dicebear.com/7.x/bottts/svg?seed=nodus", DateTime.UtcNow);

    [HttpPost("/api/auth/google")]
    public ActionResult<AuthResponse> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        return Ok(new AuthResponse("mock-jwt-token-google", CreateMockUser(), string.Empty));
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

    [HttpGet("/api/users/me")]
    public ActionResult<UserProfileDto> GetCurrentUser()
    {
        return Ok(CreateMockUser());
    }

    [HttpDelete("/api/users/me")]
    public IActionResult DeleteAccount()
    {
        return NoContent();
    }
}