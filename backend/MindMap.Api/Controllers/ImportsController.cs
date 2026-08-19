using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 导图导入接口。
/// </summary>
[ApiController]
[Authorize]
[Route("api/mindmaps/import")]
public class ImportsController : ControllerBase
{
    private readonly IImportService _import;
    private readonly ICurrentUserService _current;

    public ImportsController(IImportService import, ICurrentUserService current)
    {
        _import = import;
        _current = current;
    }

    /// <summary>
    /// 上传文件并导入为一张新的思维导图。
    /// 支持格式：.mm (FreeMind), .json/.smm (simple-mind-map), .md (Markdown), .xmind
    /// </summary>
    /// <param name="file">上传的文件（multipart/form-data 字段名: file）</param>
    /// <param name="title">导图标题，可选，默认取文件名去扩展名</param>
    /// <param name="folderId">目标文件夹 ID，可选</param>
    /// <param name="theme">主题 ID，可选，默认无</param>
    /// <param name="defaultLayout">默认布局：0=Left 1=Right 2=Top 3=Bottom 4=Radial，默认 0</param>
    /// <param name="ct">取消令牌</param>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB（更宽松的端上限，ImportService 会二次校验 5MB）
    public async Task<ApiResult<MindMapDetailDto>> ImportFile(
        IFormFile file,
        [FromForm] string? title,
        [FromForm] Guid? folderId,
        [FromForm] string? theme,
        [FromForm] int defaultLayout = 0,
        CancellationToken ct = default)
    {
        var userId = _current.UserId ?? throw ApiException.Forbidden("未登录");

        if (!Enum.IsDefined(typeof(MindMapLayout), defaultLayout))
            throw ApiException.BadRequest("非法的 defaultLayout 值");

        var layout = (MindMapLayout)defaultLayout;

        var result = await _import.ImportAsync(
            userId,
            file,
            title ?? string.Empty,
            folderId,
            theme,
            layout,
            ct);

        return ApiResult<MindMapDetailDto>.Ok(result, message: "导入成功");
    }
}
