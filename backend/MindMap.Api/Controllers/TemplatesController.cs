using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindMap.Api.Application.DTOs.Templates;
using MindMap.Api.Application.Services;
using MindMap.Api.Common.Responses;

namespace MindMap.Api.Controllers;

/// <summary>
/// 模板公共接口。普通登录用户可查看启用的模板并应用。
/// 新建/编辑/删除由 AdminTemplatesController（管理员）负责。
/// </summary>
[ApiController]
[Route("api/templates")]
[Authorize]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _svc;

    public TemplatesController(ITemplateService svc)
    {
        _svc = svc;
    }

    /// <summary>获取所有启用的模板（按 SortOrder 排序）。</summary>
    [HttpGet]
    public async Task<ApiResult<List<TemplateListItemDto>>> List(CancellationToken ct)
        => ApiResult<List<TemplateListItemDto>>.Ok(await _svc.GetEnabledListAsync(ct));

    /// <summary>获取模板详情（含完整样式 + 初始结构 JSON）。</summary>
    [HttpGet("{id:guid}")]
    public async Task<ApiResult<TemplateDetailDto>> Detail(Guid id, CancellationToken ct)
        => ApiResult<TemplateDetailDto>.Ok(await _svc.GetEnabledAsync(id, ct)
            ?? throw new MindMap.Api.Common.Exceptions.ApiException("模板不存在或未启用",
                StatusCodes.Status404NotFound));
}
