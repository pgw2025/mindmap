using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Auth;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Extensions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUserService _current;

    public AuthController(IAuthService auth, ICurrentUserService current)
    {
        _auth = auth;
        _current = current;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResult<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<ApiResult<AuthResponse>> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIp();
        var data = await _auth.RegisterAsync(req, ip, ct);
        return ApiResult<AuthResponse>.Ok(data);
    }

    [HttpPost("login")]
    public async Task<ApiResult<AuthResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIp();
        var data = await _auth.LoginAsync(req, ip, ct);
        return ApiResult<AuthResponse>.Ok(data);
    }

    [HttpPost("refresh")]
    public async Task<ApiResult<AuthResponse>> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var ip = HttpContext.GetClientIp();
        var data = await _auth.RefreshAsync(req.RefreshToken, ip, ct);
        return ApiResult<AuthResponse>.Ok(data);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ApiResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        await _auth.LogoutAsync(req.RefreshToken, ct);
        return ApiResult.Ok(message: "已登出");
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ApiResult<UserDto>> Me(CancellationToken ct)
    {
        var userId = _current.UserId ?? throw ApiException.Forbidden("未登录");
        var user = await _auth.GetCurrentUserAsync(userId, ct);
        if (user is null)
        {
            throw ApiException.NotFound("User", userId);
        }
        return ApiResult<UserDto>.Ok(user);
    }
}
