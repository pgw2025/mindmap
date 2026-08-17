using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Nodes;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api/mindmaps/{mindMapId:guid}/nodes")]
public class NodesController : ControllerBase
{
    private readonly INodeService _svc;
    private readonly ICurrentUserService _current;

    public NodesController(INodeService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    /// <summary>获取导图所有节点（扁平列表）。</summary>
    [HttpGet]
    public async Task<ApiResult<List<NodeDto>>> List(Guid mindMapId, CancellationToken ct)
    {
        var data = await _svc.GetByMindMapAsync(_current.UserId, mindMapId, ct);
        return ApiResult<List<NodeDto>>.Ok(data);
    }

    /// <summary>获取导图节点树（递归结构）。</summary>
    [HttpGet("tree")]
    public async Task<ApiResult<List<NodeTreeNodeDto>>> Tree(Guid mindMapId, CancellationToken ct)
    {
        var data = await _svc.GetTreeAsync(_current.UserId, mindMapId, ct);
        return ApiResult<List<NodeTreeNodeDto>>.Ok(data);
    }

    /// <summary>获取单个节点。</summary>
    [HttpGet("{id:guid}")]
    public async Task<ApiResult<NodeDto>> Get(Guid mindMapId, Guid id, CancellationToken ct)
    {
        var data = await _svc.GetAsync(_current.UserId, id, ct);
        if (data is null) throw ApiException.NotFound("Node", id);
        return ApiResult<NodeDto>.Ok(data);
    }

    /// <summary>创建节点。</summary>
    [Authorize]
    [HttpPost]
    public async Task<ApiResult<NodeDto>> Create(Guid mindMapId, [FromBody] NodeCreateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.CreateAsync(userId, mindMapId, req, ct);
        return ApiResult<NodeDto>.Ok(data);
    }

    /// <summary>更新节点（部分字段）。</summary>
    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<ApiResult<NodeDto>> Update(Guid mindMapId, Guid id, [FromBody] NodeUpdateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.UpdateAsync(userId, id, req, ct);
        return ApiResult<NodeDto>.Ok(data);
    }

    /// <summary>移动节点（变更父节点 + 排序）。</summary>
    [Authorize]
    [HttpPost("{id:guid}/move")]
    public async Task<ApiResult<NodeDto>> Move(Guid mindMapId, Guid id, [FromBody] NodeMoveRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.MoveAsync(userId, id, req, ct);
        return ApiResult<NodeDto>.Ok(data);
    }

    /// <summary>批量更新节点（拖拽排序、批量样式）。</summary>
    [Authorize]
    [HttpPut("batch")]
    public async Task<ApiResult> BatchUpdate(Guid mindMapId, [FromBody] NodeBatchUpdateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.BatchUpdateAsync(userId, mindMapId, req, ct);
        return ApiResult.Ok(message: "已批量更新");
    }

    /// <summary>删除节点（递归删除子孙）。</summary>
    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid mindMapId, Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _svc.DeleteAsync(userId, id, ct);
        return ApiResult.Ok(message: "节点已删除");
    }

    private Guid RequireUserId()
        => _current.UserId ?? throw ApiException.Forbidden("未登录");
}
