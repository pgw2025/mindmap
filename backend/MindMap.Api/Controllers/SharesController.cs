using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Shares;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

[ApiController]
[Route("api")]
public class SharesController : ControllerBase
{
    private readonly IShareService _service;
    private readonly ICurrentUserService _current;

    public SharesController(IShareService service, ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    [Authorize]
    [HttpGet("mindmaps/{mindMapId:guid}/shares")]
    public async Task<ApiResult<List<ShareListDto>>> GetShares([FromRoute] Guid mindMapId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var list = await _service.GetSharesAsync(userId, mindMapId, ct);
        return ApiResult<List<ShareListDto>>.Ok(list);
    }

    [Authorize]
    [HttpPost("mindmaps/{mindMapId:guid}/shares")]
    public async Task<ApiResult<ShareDto>> CreateShare([FromRoute] Guid mindMapId, [FromBody] ShareCreateRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var dto = await _service.CreateShareAsync(userId, mindMapId, req, ct);
        return ApiResult<ShareDto>.Ok(dto);
    }

    [Authorize]
    [HttpDelete("shares/{shareId:guid}")]
    public async Task<ApiResult> DeleteShare([FromRoute] Guid shareId, CancellationToken ct)
    {
        var userId = RequireUserId();
        await _service.DeleteShareAsync(userId, shareId, ct);
        return ApiResult.Ok(message: "已删除分享链接");
    }

    [AllowAnonymous]
    [HttpPost("shares/verify")]
    public async Task<ApiResult<ShareVerifyResponse>> VerifyShareToken([FromBody] ShareVerifyRequest req, CancellationToken ct)
    {
        var result = await _service.VerifyShareTokenAsync(req.Token, req.Password, ct);
        return ApiResult<ShareVerifyResponse>.Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("shares/{token}/mindmap")]
    public async Task<ApiResult<ShareMindMapResponse>> GetSharedMindMap([FromRoute] string token, [FromQuery] string? password, CancellationToken ct)
    {
        var result = await _service.GetSharedMindMapAsync(token, password, ct);
        return ApiResult<ShareMindMapResponse>.Ok(result);
    }

    private Guid RequireUserId()
        => _current.UserId ?? throw ApiException.Forbidden("未登录");
}
