using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;

namespace Nodus.Api.Controllers;

[ApiController]
public class AuthController : ControllerBase
{
    private static UserProfileDto CreateMockUser(string email = "student@knu.ua", string nickname = "NodusUser") =>
        new(Guid.NewGuid(), email, nickname, "https://api.dicebear.com/7.x/bottts/svg?seed=nodus", DateTime.UtcNow);

    [HttpPost("/api/auth/google")]
    public ActionResult<AuthResponse> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        return Ok(new AuthResponse("mock-jwt-token-google", CreateMockUser()));
    }

    [HttpPost("/api/auth/register")]
    public ActionResult<AuthResponse> Register([FromBody] RegisterRequest request)
    {
        return Ok(new AuthResponse("mock-jwt-token-register", CreateMockUser(request.Email, request.Nickname)));
    }

    [HttpPost("/api/auth/login")]
    public ActionResult<AuthResponse> Login([FromBody] LoginRequest request)
    {
        return Ok(new AuthResponse("mock-jwt-token-login", CreateMockUser(request.Email)));
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