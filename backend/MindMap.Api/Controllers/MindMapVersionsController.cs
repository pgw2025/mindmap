using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Versions;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api/mindmaps/{mindMapId:guid}/versions")]
public class MindMapVersionsController : ControllerBase
{
    private readonly IMindMapVersionService _service;
    private readonly ICurrentUserService _current;

    public MindMapVersionsController(IMindMapVersionService service, ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    /// <summary>获取导图的版本历史列表（按版本号倒序）。</summary>
    [HttpGet]
    public async Task<ApiResult<List<MindMapVersionDto>>> List([FromRoute] Guid mindMapId, CancellationToken ct)
    {
        var list = await _service.ListAsync(_current.UserId, mindMapId, ct);
        return ApiResult<List<MindMapVersionDto>>.Ok(list);
    }

    /// <summary>创建版本快照。</summary>
    [HttpPost]
    [Authorize]
    public async Task<ApiResult<MindMapVersionDto>> Create([FromRoute] Guid mindMapId, [FromBody] CreateVersionRequest req, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(_current.UserId!.Value, mindMapId, req, ct);
        return ApiResult<MindMapVersionDto>.Ok(dto);
    }

    /// <summary>回滚到指定版本（会替换当前节点树）。</summary>
    [HttpPost("{versionId:guid}/rollback")]
    [Authorize]
    public async Task<ApiResult<string>> Rollback([FromRoute] Guid mindMapId, [FromRoute] Guid versionId, CancellationToken ct)
    {
        await _service.RollbackAsync(_current.UserId!.Value, mindMapId, versionId, ct);
        return ApiResult<string>.Ok("已回滚");
    }

    /// <summary>删除版本快照。</summary>
    [HttpDelete("{versionId:guid}")]
    [Authorize]
    public async Task<ApiResult<string>> Delete([FromRoute] Guid mindMapId, [FromRoute] Guid versionId, CancellationToken ct)
    {
        await _service.DeleteAsync(_current.UserId!.Value, mindMapId, versionId, ct);
        return ApiResult<string>.Ok("已删除");
    }
}
