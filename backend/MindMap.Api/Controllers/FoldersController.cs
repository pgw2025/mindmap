using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Folders;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/folders")]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _svc;
    private readonly ICurrentUserService _current;

    public FoldersController(IFolderService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet("tree")]
    public async Task<ApiResult<IReadOnlyList<FolderNodeDto>>> GetTree(CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.GetTreeAsync(userId, ct);
        return ApiResult<IReadOnlyList<FolderNodeDto>>.Ok(data);
    }

    [HttpPost]
    public async Task<ApiResult<FolderDto>> Create([FromBody] FolderCreateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.CreateAsync(userId, req, ct);
        return ApiResult<FolderDto>.Ok(data);
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResult<FolderDto>> Update(Guid id, [FromBody] FolderUpdateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.UpdateAsync(userId, id, req, ct);
        return ApiResult<FolderDto>.Ok(data);
    }

    [HttpPost("{id:guid}/move")]
    public async Task<ApiResult> Move(Guid id, [FromBody] FolderMoveRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.MoveAsync(userId, id, req, ct);
        return ApiResult.Ok(message: "已移动");
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.DeleteAsync(userId, id, ct);
        return ApiResult.Ok(message: "已删除");
    }

    private Guid RequireUserId()
        => _current.UserId ?? throw ApiException.Forbidden("未登录");
}
