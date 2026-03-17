using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace StockAnalysis.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string returnUrl = "http://localhost:5173")
    {
        var props = new AuthenticationProperties
        {
            RedirectUri = $"/api/auth/callback?returnUrl={Uri.EscapeDataString(returnUrl)}"
        };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string returnUrl = "http://localhost:5173")
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded)
            return Redirect($"{returnUrl}?error=auth_failed");

        return Redirect(returnUrl);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var name = User.FindFirstValue(ClaimTypes.Name);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var picture = User.FindFirstValue("urn:google:picture")
                   ?? User.FindFirstValue("picture");

        return Ok(new { name, email, picture });
    }
}
