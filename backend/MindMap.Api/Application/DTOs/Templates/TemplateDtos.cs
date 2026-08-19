using System.ComponentModel.DataAnnotations;
using MindMap.Api.Application.DTOs.Admin;

namespace MindMap.Api.Application.DTOs.Templates;

// ===================== 公共 DTO（普通用户可见） =====================

/// <summary>模板列表项（不含完整 JSON，仅展示信息）。</summary>
public class TemplateListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public string? SwatchJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>模板详情（含完整样式 + 初始结构 JSON，用于应用模板）。</summary>
public class TemplateDetailDto : TemplateListItemDto
{
    public string ConfigJson { get; set; } = string.Empty;
    public string InitialStructureJson { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public Guid? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ===================== 管理端 DTO =====================

public class AdminTemplateListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
    public string? SwatchJson { get; set; }
    public Guid? CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdminTemplateListQuery : AdminPagedQuery
{
    /// <summary>"all" | "enabled" | "disabled"</summary>
    public string Scope { get; set; } = "all";
}

public class TemplateCreateRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>完整 MindMapThemeConfig JSON。</summary>
    [Required]
    public string ConfigJson { get; set; } = string.Empty;

    /// <summary>初始节点结构 JSON，可为空字符串。</summary>
    public string InitialStructureJson { get; set; } = string.Empty;

    /// <summary>缩略图色板 JSON（rootFill/secondFill/lineColor/bg）。</summary>
    [StringLength(512)]
    public string? SwatchJson { get; set; }
}

public class TemplateUpdateRequest
{
    [StringLength(64, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }

    public int? SortOrder { get; set; }

    public bool? IsEnabled { get; set; }

    public string? ConfigJson { get; set; }

    public string? InitialStructureJson { get; set; }

    [StringLength(512)]
    public string? SwatchJson { get; set; }
}
