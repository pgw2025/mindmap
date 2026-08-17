using System.Text;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.Services;
using MindMap.Api.Security;

namespace MindMap.Api.Controllers;

/// <summary>
/// 导图导出接口。
/// </summary>
[ApiController]
[Route("api/mindmaps/{mindMapId:guid}/export")]
public class ExportsController : ControllerBase
{
    private readonly IExportService _export;
    private readonly ICurrentUserService _current;

    public ExportsController(IExportService export, ICurrentUserService current)
    {
        _export = export;
        _current = current;
    }

    /// <summary>
    /// 导出为 FreeMind (.mm) 格式。
    /// 公开导图匿名可导出；私有导图需登录且为所有者。
    /// </summary>
    [HttpGet("freemind")]
    public async Task<IActionResult> ExportFreeMind(Guid mindMapId, CancellationToken ct)
    {
        var xml = await _export.ExportFreeMindAsync(_current.UserId, mindMapId, ct);
        var bytes = Encoding.UTF8.GetBytes(xml);
        var fileName = $"{mindMapId}.mm";
        return File(bytes, "application/xml", fileName);
    }
}
