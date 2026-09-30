using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.RecycleBin;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 回收站：软删除导图的列表、恢复、彻底删除与保留天数设置。
/// </summary>
[ApiController]
[Authorize]
[Route("api/recycle-bin")]
public class RecycleBinController : ControllerBase
{
    private readonly IRecycleBinService _svc;
    private readonly ICurrentUserService _current;

    public RecycleBinController(IRecycleBinService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    /// <summary>回收站列表（分页 + 关键字，含剩余保留天数）。</summary>
    [HttpGet]
    public async Task<ApiResult<PagedResult<RecycleBinItemDto>>> List([FromQuery] RecycleBinListQuery query, CancellationToken ct)
    {
        var data = await _svc.GetListAsync(RequireUserId(), query, ct);
        return ApiResult<PagedResult<RecycleBinItemDto>>.Ok(data);
    }

    /// <summary>恢复导图（清空 DeletedAt，回到原列表位置）。</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ApiResult> Restore(Guid id, CancellationToken ct)
    {
        await _svc.RestoreAsync(RequireUserId(), id, ct);
        return ApiResult.Ok(message: "已恢复");
    }

    /// <summary>彻底删除（物理清除节点与导图，不可恢复）。</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(RequireUserId(), id, ct);
        return ApiResult.Ok(message: "已彻底删除");
    }

    /// <summary>获取回收站保留天数设置。</summary>
    [HttpGet("settings")]
    public async Task<ApiResult<RecycleBinSettingsDto>> GetSettings(CancellationToken ct)
    {
        var data = await _svc.GetSettingsAsync(RequireUserId(), ct);
        return ApiResult<RecycleBinSettingsDto>.Ok(data);
    }

    /// <summary>设置回收站保留天数（null = 恢复全局默认）。</summary>
    [HttpPut("settings")]
    public async Task<ApiResult<RecycleBinSettingsDto>> UpdateSettings([FromBody] RecycleBinSettingsUpdateRequest req, CancellationToken ct)
    {
        var data = await _svc.UpdateSettingsAsync(RequireUserId(), req, ct);
        return ApiResult<RecycleBinSettingsDto>.Ok(data);
    }

    private Guid RequireUserId()
        => _current.UserId ?? throw Common.Exceptions.ApiException.Forbidden("未登录");
}
