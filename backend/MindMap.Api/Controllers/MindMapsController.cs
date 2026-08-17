using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api/mindmaps")]
public class MindMapsController : ControllerBase
{
    private readonly IMindMapService _svc;
    private readonly ICurrentUserService _current;

    public MindMapsController(IMindMapService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    public async Task<ApiResult<PagedResult<MindMapListItemDto>>> List([FromQuery] MindMapListQuery query, CancellationToken ct)
    {
        // 公开列表匿名可访问；我的列表必须登录
        var isPublicScope = string.Equals(query.Scope, "public", StringComparison.OrdinalIgnoreCase);
        var userId = _current.UserId;
        if (!isPublicScope && userId is null)
            throw ApiException.Forbidden("未登录");

        var data = await _svc.GetListAsync(userId, query, ct);
        return ApiResult<PagedResult<MindMapListItemDto>>.Ok(data);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResult<MindMapDetailDto>> Get(Guid id, CancellationToken ct)
    {
        // 公开导图匿名可读；私有导图需登录 + 所有者
        var userId = _current.UserId;
        var data = await _svc.GetAsync(userId, id, ct);
        if (data is null) throw ApiException.NotFound("MindMap", id);
        return ApiResult<MindMapDetailDto>.Ok(data);
    }

    [Authorize]
    [HttpPost]
    public async Task<ApiResult<MindMapDetailDto>> Create([FromBody] MindMapCreateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.CreateAsync(userId, req, ct);
        return ApiResult<MindMapDetailDto>.Ok(data);
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<ApiResult<MindMapDetailDto>> Update(Guid id, [FromBody] MindMapUpdateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.UpdateAsync(userId, id, req, ct);
        return ApiResult<MindMapDetailDto>.Ok(data);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.DeleteAsync(userId, id, ct);
        return ApiResult.Ok(message: "已删除");
    }

    [Authorize]
    [HttpPost("{id:guid}/copy")]
    public async Task<ApiResult<MindMapDetailDto>> Copy(Guid id, [FromBody] MindMapCopyRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.CopyAsync(userId, id, req, ct);
        return ApiResult<MindMapDetailDto>.Ok(data);
    }

    [Authorize]
    [HttpPut("{id:guid}/tags")]
    public async Task<ApiResult> SetTags(Guid id, [FromBody] MindMapTagsRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.SetTagsAsync(userId, id, req.TagIds, ct);
        return ApiResult.Ok(message: "标签已更新");
    }

    private Guid RequireUserId()
        => _current.UserId ?? throw ApiException.Forbidden("未登录");
}
