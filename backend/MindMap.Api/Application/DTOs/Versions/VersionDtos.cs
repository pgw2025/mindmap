namespace MindMap.Api.Application.DTOs.Versions;

public record MindMapVersionDto(
    Guid Id,
    int VersionNumber,
    string? Remark,
    int NodeCount,
    Guid CreatedById,
    string CreatedByName,
    DateTime CreatedAt
);

public record CreateVersionRequest(
    string? Remark
);

public record VersionNodeData(
    Guid Id,
    Guid? ParentId,
    string Title,
    string? Content,
    string? Note,
    int SortOrder,
    bool IsCollapsed,
    double? X,
    double? Y,
    double? Width,
    double? Height,
    string? Color,
    int? FontSize,
    string? FontFamily,
    string? Icon,
    string? BorderColor,
    string? BackgroundColor,
    string? EdgeColor,
    int? Shape,
    int? EdgeStyle,
    string? ExtraData,
    List<VersionNodeData>? Children
);
