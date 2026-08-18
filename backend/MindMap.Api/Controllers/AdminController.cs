using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Admin;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _svc;
    private readonly ICurrentUserService _current;

    public AdminController(IAdminService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    // ---------- 统计 ----------
    [HttpGet("stats")]
    public async Task<ApiResult<AdminStatsDto>> Stats(CancellationToken ct)
        => ApiResult<AdminStatsDto>.Ok(await _svc.GetStatsAsync(ct));

    // ---------- 用户 ----------
    [HttpGet("users")]
    public async Task<ApiResult<PagedResult<AdminUserListItemDto>>> Users([FromQuery] AdminUserListQuery query, CancellationToken ct)
        => ApiResult<PagedResult<AdminUserListItemDto>>.Ok(await _svc.GetUsersAsync(query, ct));

    [HttpPut("users/{id:guid}")]
    public async Task<ApiResult> UpdateUser(Guid id, [FromBody] AdminUserUpdateRequest req, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.UpdateUserAsync(id, req, op, ct);
        return ApiResult.Ok(message: "已更新");
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<ApiResult> DeleteUser(Guid id, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.DeleteUserAsync(id, op, ct);
        return ApiResult.Ok(message: "已删除");
    }

    // ---------- 导图 ----------
    [HttpGet("mindmaps")]
    public async Task<ApiResult<PagedResult<AdminMindMapListItemDto>>> MindMaps([FromQuery] AdminMindMapListQuery query, CancellationToken ct)
        => ApiResult<PagedResult<AdminMindMapListItemDto>>.Ok(await _svc.GetMindMapsAsync(query, ct));

    [HttpPost("mindmaps/{id:guid}/takedown")]
    public async Task<ApiResult> TakeDownMindMap(Guid id, [FromBody] AdminMindMapTakeDownRequest req, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.TakeDownMindMapAsync(id, req, op, ct);
        return ApiResult.Ok(message: "已下架");
    }

    [HttpPost("mindmaps/{id:guid}/restore")]
    public async Task<ApiResult> RestoreMindMap(Guid id, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.RestoreMindMapAsync(id, op, ct);
        return ApiResult.Ok(message: "已恢复");
    }

    [HttpDelete("mindmaps/{id:guid}")]
    public async Task<ApiResult> DeleteMindMap(Guid id, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.DeleteMindMapAsync(id, op, ct);
        return ApiResult.Ok(message: "已删除");
    }

    // ---------- 举报 ----------
    [HttpGet("reports")]
    public async Task<ApiResult<PagedResult<AdminReportListItemDto>>> Reports([FromQuery] AdminReportListQuery query, CancellationToken ct)
        => ApiResult<PagedResult<AdminReportListItemDto>>.Ok(await _svc.GetReportsAsync(query, ct));

    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<ApiResult> ResolveReport(Guid id, [FromBody] AdminReportResolveRequest req, CancellationToken ct)
    {
        RequireAdmin(out var op);
        await _svc.ResolveReportAsync(id, req, op, ct);
        return ApiResult.Ok(message: "已处理");
    }

    private void RequireAdmin(out Guid op)
    {
        op = _current.UserId ?? throw ApiException.Forbidden("未登录");
        if (!_current.IsAdmin) throw ApiException.Forbidden("需要管理员权限");
    }
}
