using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Tags;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/tags")]
public class TagsController : ControllerBase
{
    private readonly ITagService _svc;
    private readonly ICurrentUserService _current;

    public TagsController(ITagService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<TagDto>>> GetAll(CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.GetAllAsync(userId, ct);
        return ApiResult<IReadOnlyList<TagDto>>.Ok(data);
    }

    [HttpPost]
    public async Task<ApiResult<TagDto>> Create([FromBody] TagCreateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.CreateAsync(userId, req, ct);
        return ApiResult<TagDto>.Ok(data);
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResult<TagDto>> Update(Guid id, [FromBody] TagUpdateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var data = await _svc.UpdateAsync(userId, id, req, ct);
        return ApiResult<TagDto>.Ok(data);
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
