using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MindMap.Api.Application.DTOs.Uploads;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Helpers;
using MindMap.Api.Common.Options;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 通用文件上传接口。
/// </summary>
[ApiController]
[Authorize]
[Route("api/uploads")]
public class UploadsController : ControllerBase
{
    private const int MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB

    private readonly IOptions<AppOptions> _appOptions;
    private readonly ICurrentUserService _current;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(
        IOptions<AppOptions> appOptions,
        ICurrentUserService current,
        IWebHostEnvironment env,
        ILogger<UploadsController> logger)
    {
        _appOptions = appOptions;
        _current = current;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// 上传节点图片。
    /// 校验：≤5MB、扩展名白名单（jpg/png/gif/webp）、文件头魔数；
    /// 存储：{UploadRoot}/nodes/{yyyyMM}/{guid}{ext}，按月分桶；
    /// 返回站内相对 URL 与原始宽高（宽高探测失败时前端用 Image.onload 兜底）。
    /// </summary>
    /// <param name="file">图片文件（multipart/form-data 字段名: file）</param>
    /// <param name="ct">取消令牌</param>
    [HttpPost("node-image")]
    [RequestSizeLimit(MaxImageSizeBytes)]
    public async Task<ApiResult<NodeImageUploadResultDto>> UploadNodeImage(IFormFile file, CancellationToken ct)
    {
        var userId = _current.UserId ?? throw ApiException.Forbidden("未登录");

        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("请选择要上传的图片");

        if (file.Length > MaxImageSizeBytes)
            throw ApiException.BadRequest("图片大小不能超过 5MB");

        if (!ImageFileHelper.IsAllowedExtension(file.FileName))
            throw ApiException.BadRequest("仅支持 jpg / png / gif / webp 格式");

        // 整体读入内存（已限 5MB），用于魔数校验与尺寸探测
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        if (!ImageFileHelper.HasValidMagic(bytes))
            throw ApiException.BadRequest("文件内容不是有效的图片");

        var size = ImageFileHelper.TryGetSize(bytes);

        // 存储路径：{UploadRoot}/nodes/{yyyyMM}/{guid}{ext}
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var month = DateTime.UtcNow.ToString("yyyyMM");
        var fileName = $"{Guid.NewGuid():N}{ext}";

        var uploadRoot = ResolveUploadRoot();
        var targetDir = Path.Combine(uploadRoot, "nodes", month);
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, fileName);
        await System.IO.File.WriteAllBytesAsync(targetPath, bytes, ct);

        var url = $"/uploads/nodes/{month}/{fileName}";
        _logger.LogInformation("用户 {UserId} 上传节点图片：{Url}", userId, url);

        return ApiResult<NodeImageUploadResultDto>.Ok(new NodeImageUploadResultDto
        {
            Url = url,
            Width = size?.Width ?? 0,
            Height = size?.Height ?? 0
        }, message: "上传成功");
    }

    /// <summary>
    /// 解析上传根目录绝对路径。
    /// AppOptions.UploadRoot 相对 ContentRoot 解析（默认 wwwroot/uploads）。
    /// </summary>
    private string ResolveUploadRoot()
    {
        var root = _appOptions.Value.UploadRoot;
        return Path.IsPathRooted(root)
            ? root
            : Path.Combine(_env.ContentRootPath, root);
    }
}
