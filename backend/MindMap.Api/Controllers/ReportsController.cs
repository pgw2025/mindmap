using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Admin;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 用户端举报提交。任何登录用户可对任意导图提交一次举报。
/// 管理员审核接口位于 AdminController。
/// </summary>
[ApiController]
[Route("api/mindmaps/{mindMapId:guid}/reports")]
public class ReportsController : ControllerBase
{
    private readonly IAdminService _admin;
    private readonly ICurrentUserService _current;

    public ReportsController(IAdminService admin, ICurrentUserService current)
    {
        _admin = admin;
        _current = current;
    }

    [Authorize]
    [HttpPost]
    public async Task<ApiResult<AdminReportListItemDto>> Create(Guid mindMapId, [FromBody] AdminReportCreateRequest req, CancellationToken ct)
    {
        var userId = _current.UserId ?? throw ApiException.Forbidden("未登录");
        var dto = await _admin.CreateReportAsync(userId, mindMapId, req, ct);
        return ApiResult<AdminReportListItemDto>.Ok(dto, message: "举报已提交，管理员将在后台审核");
    }
}
