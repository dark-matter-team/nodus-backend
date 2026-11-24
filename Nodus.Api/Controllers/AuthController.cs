using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nodus.Api.DTOs;
using Nodus.Api.Extensions;
using Nodus.Api.Services;

namespace Nodus.Api.Controllers;

[ApiController]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("/api/auth/google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin([FromBody] GoogleAuthRequest request)
    {
        return Ok(await authService.GoogleLoginAsync(request.IdToken));
    }

    [HttpPost("/api/auth/register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        return Ok(await authService.RegisterAsync(request.Email, request.Password, request.Nickname));
    }

    [HttpPost("/api/auth/login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        return Ok(await authService.LoginAsync(request.Email, request.Password, request.Nickname));
    }

    [Authorize]
    [HttpGet("/api/users/me")]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser()
    {
        return Ok(await authService.GetUserProfileAsync(User.GetUserId()));
    }

    [Authorize]
    [HttpDelete("/api/users/me")]
    public async Task<IActionResult> DeleteAccount()
    {
        await authService.DeleteUserAccountAsync(User.GetUserId());
        return NoContent();
    }

    [HttpPost("/api/auth/forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.ForgotPasswordAsync(request.Email, HttpContext.RequestAborted);
        return Ok(new { message = "If this email is registered, a reset code has been sent." });
    }

    [HttpPost("/api/auth/reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await authService.ResetPasswordAsync(request.Email, request.Code, request.NewPassword);
        return Ok(new { message = "Password has been reset successfully." });
    }

    [HttpPost("/api/auth/verify-reset-code")]
    public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeRequest request)
    {
        await authService.VerifyResetCodeAsync(request.Email, request.Code);
        return Ok(new { message = "Reset code is valid." });
    }
}