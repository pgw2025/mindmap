using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Common.Responses;

namespace MindMap.Api.Controllers;

/// <summary>
/// 健康检查接口，用于前端启动时验证后端可达。
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ApiResult Get()
    {
        return ApiResult.Ok(new
        {
            service = "MindMap.Api",
            status = "ok",
            timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("auth")]
    [Authorize]
    public ApiResult AuthCheck()
    {
        var user = User.Identity?.Name ?? "unknown";
        return ApiResult.Ok(new { user, authenticated = true });
    }
}
