namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 思维导图模板。仅管理员可创建/编辑，所有登录用户可查看启用的模板。
/// 模板包含：节点 4 级样式 + 连线样式 + 可选初始节点结构。
/// </summary>
public class Template
{
    public Guid Id { get; set; }

    /// <summary>模板名称（如 "商务蓝"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模板描述。</summary>
    public string? Description { get; set; }

    /// <summary>排序值，越小越靠前。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用。禁用后普通用户不可见。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 样式 JSON：完整 MindMapThemeConfig（节点 4 级 + 连线 + 背景）。
    /// 由前端 presets.ts 的 MindMapThemeConfig 结构定义。
    /// </summary>
    public string ConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// 初始节点结构 JSON（simple-mind-map 的 data 树结构）。
    /// 可为空字符串表示无初始结构（仅应用样式）。
    /// </summary>
    public string InitialStructureJson { get; set; } = string.Empty;

    /// <summary>缩略图色板，用于列表预览：rootFill / secondFill / lineColor / bg。</summary>
    public string? SwatchJson { get; set; }

    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
