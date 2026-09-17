using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Templates;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 模板管理接口（仅管理员）。新建/编辑/删除/分页查询。
/// </summary>
[ApiController]
[Route("api/admin/templates")]
[Authorize(Policy = "AdminOnly")]
public class AdminTemplatesController : ControllerBase
{
    private readonly ITemplateService _svc;
    private readonly ICurrentUserService _current;

    public AdminTemplatesController(ITemplateService svc, ICurrentUserService current)
    {
        _svc = svc;
        _current = current;
    }

    [HttpGet]
    public async Task<ApiResult<PagedResult<AdminTemplateListItemDto>>> List([FromQuery] AdminTemplateListQuery query, CancellationToken ct)
        => ApiResult<PagedResult<AdminTemplateListItemDto>>.Ok(await _svc.GetAdminListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ApiResult<TemplateDetailDto>> Detail(Guid id, CancellationToken ct)
        => ApiResult<TemplateDetailDto>.Ok(await _svc.GetAdminAsync(id, ct));

    [HttpPost]
    public async Task<ApiResult<TemplateDetailDto>> Create([FromBody] TemplateCreateRequest req, CancellationToken ct)
    {
        RequireAdmin(out var op);
        return ApiResult<TemplateDetailDto>.Ok(await _svc.CreateAsync(op, req, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResult<TemplateDetailDto>> Update(Guid id, [FromBody] TemplateUpdateRequest req, CancellationToken ct)
    {
        RequireAdmin(out _);
        return ApiResult<TemplateDetailDto>.Ok(await _svc.UpdateAsync(id, req, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResult> Delete(Guid id, CancellationToken ct)
    {
        RequireAdmin(out _);
        await _svc.DeleteAsync(id, ct);
        return ApiResult.Ok(message: "已删除");
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportAll(CancellationToken ct)
    {
        RequireAdmin(out _);
        var file = await _svc.ExportAllAsync(ct);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"templates-{DateTime.UtcNow:yyyyMMdd-HHmmss}.mmtpl.json";
        return File(bytes, "application/json", fileName);
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        RequireAdmin(out _);
        var file = await _svc.ExportAsync(id, ct);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var safeName = SanitizeFileName(file.Template?.Name) ?? "template";
        var fileName = $"{safeName}.mmtpl.json";
        return File(bytes, "application/json", fileName);
    }

    [HttpPost("import")]
    public async Task<ApiResult<TemplateImportResult>> Import([FromForm] IFormFile file, CancellationToken ct)
    {
        RequireAdmin(out _);
        return ApiResult<TemplateImportResult>.Ok(await _svc.ImportAsync(file, ct));
    }

    private void RequireAdmin(out Guid op)
    {
        op = _current.UserId ?? throw ApiException.Forbidden("未登录");
        if (!_current.IsAdmin) throw ApiException.Forbidden("需要管理员权限");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string? SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
