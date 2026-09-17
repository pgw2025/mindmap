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

// ===================== 导入导出 DTO =====================

/// <summary>
/// 导出文件顶层结构（.mmtpl.json）。
/// 单条导出时 "template" 为一个对象；批量导出时用 <see cref="TemplateExportBatchFile"/>。
/// </summary>
public class TemplateExportFile
{
    /// <summary>格式标识，用于导入时校验，固定为 "mindmap-template"。</summary>
    public string Format { get; set; } = "mindmap-template";

    /// <summary>文件结构版本，当前仅支持 1。</summary>
    public int Version { get; set; } = 1;

    /// <summary>导出时间（UTC）。</summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>单个模板内容。</summary>
    public TemplateExportPayload? Template { get; set; }
}

/// <summary>批量导出文件顶层结构：template 换成 templates 数组。</summary>
public class TemplateExportBatchFile
{
    public string Format { get; set; } = "mindmap-template";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public List<TemplateExportPayload> Templates { get; set; } = new();
}

/// <summary>导出的模板本体（不含 Id/CreatedAt 等服务端私有字段）。</summary>
public class TemplateExportPayload
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>完整 MindMapThemeConfig，以结构化对象存储。</summary>
    public System.Text.Json.JsonElement ConfigJson { get; set; }

    /// <summary>初始节点树，可为 null（仅样式模板）。</summary>
    public System.Text.Json.JsonElement? InitialStructureJson { get; set; }

    /// <summary>缩略图色板，可为 null（导入时自动重建）。</summary>
    public System.Text.Json.JsonElement? SwatchJson { get; set; }
}

/// <summary>导入报告。</summary>
public class TemplateImportResult
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<TemplateImportFailedItem> Failed { get; set; } = new();
    public int Total => Created + Skipped + Failed.Count;
}

/// <summary>单条导入失败明细。</summary>
public class TemplateImportFailedItem
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
