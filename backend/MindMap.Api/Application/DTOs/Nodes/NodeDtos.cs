using System.ComponentModel.DataAnnotations;
using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Application.DTOs.Nodes;

/// <summary>
/// 创建节点请求。
/// </summary>
public class NodeCreateRequest
{
    public Guid? ParentId { get; set; }

    [Required, StringLength(512, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(16384)]
    public string? Content { get; set; }

    [StringLength(4096)]
    public string? Note { get; set; }

    /// <summary>同级排序，0 = 第一个；省略时追加到末尾。</summary>
    public int? SortOrder { get; set; }

    public bool IsCollapsed { get; set; }

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    [StringLength(32)]
    public string? Color { get; set; }
    public int? FontSize { get; set; }
    [StringLength(64)]
    public string? FontFamily { get; set; }
    public NodeShape? Shape { get; set; }
    [StringLength(128)]
    public string? Icon { get; set; }
    [StringLength(32)]
    public string? BorderColor { get; set; }
    [StringLength(32)]
    public string? BackgroundColor { get; set; }
    [StringLength(32)]
    public string? EdgeColor { get; set; }
    public EdgeStyle? EdgeStyle { get; set; }

    /// <summary>根节点直接子节点的生长方向；null 表示前端默认朝右。</summary>
    public Direction? Direction { get; set; }

    /// <summary>JSON 扩展数据（附件、链接等）。</summary>
    [StringLength(32768)]
    public string? ExtraData { get; set; }
}

/// <summary>
/// 更新节点请求。所有字段可选，仅更新传入字段。
/// </summary>
public class NodeUpdateRequest
{
    [StringLength(512, MinimumLength = 1)]
    public string? Title { get; set; }

    [StringLength(16384)]
    public string? Content { get; set; }

    [StringLength(4096)]
    public string? Note { get; set; }

    public int? SortOrder { get; set; }
    public bool? IsCollapsed { get; set; }

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    [StringLength(32)]
    public string? Color { get; set; }
    public int? FontSize { get; set; }
    [StringLength(64)]
    public string? FontFamily { get; set; }
    public NodeShape? Shape { get; set; }
    [StringLength(128)]
    public string? Icon { get; set; }
    [StringLength(32)]
    public string? BorderColor { get; set; }
    [StringLength(32)]
    public string? BackgroundColor { get; set; }
    [StringLength(32)]
    public string? EdgeColor { get; set; }
    public EdgeStyle? EdgeStyle { get; set; }

    public Direction? Direction { get; set; }

    [StringLength(32768)]
    public string? ExtraData { get; set; }
}

/// <summary>
/// 移动节点请求：变更父节点 + 同级排序。
/// </summary>
public class NodeMoveRequest
{
    /// <summary>新父节点 Id；为 null 表示提升为根级节点。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>目标同级排序位置；省略时追加到末尾。</summary>
    public int? SortOrder { get; set; }
}

/// <summary>
/// 批量更新节点请求（用于拖拽排序、批量改样式等）。
/// </summary>
public class NodeBatchUpdateRequest
{
    [Required]
    public List<NodeBatchItem> Nodes { get; set; } = new();
}

public class NodeBatchItem
{
    [Required]
    public Guid Id { get; set; }

    public int? SortOrder { get; set; }
    public Guid? ParentId { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool? IsCollapsed { get; set; }
}

/// <summary>
/// 节点 DTO（用于详情/列表返回）。
/// </summary>
public class NodeDto
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public bool IsCollapsed { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Color { get; set; }
    public int? FontSize { get; set; }
    public string? FontFamily { get; set; }
    public int? Shape { get; set; }
    public string? Icon { get; set; }
    public string? BorderColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? EdgeColor { get; set; }
    public EdgeStyle? EdgeStyle { get; set; }
    public Direction? Direction { get; set; }
    public string? ExtraData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 节点树（含子节点）。
/// </summary>
public class NodeTreeNodeDto : NodeDto
{
    public List<NodeTreeNodeDto> Children { get; set; } = new();
}
