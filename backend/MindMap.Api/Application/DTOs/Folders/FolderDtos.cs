using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Folders;

public class FolderCreateRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int? SortOrder { get; set; }
}

public class FolderUpdateRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public int? SortOrder { get; set; }
}

public class FolderMoveRequest
{
    /// <summary>新父级 Id；为 null 表示移到根级。</summary>
    public Guid? ParentId { get; set; }
}

public class FolderDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FolderNodeDto : FolderDto
{
    public List<FolderNodeDto> Children { get; set; } = new();
    public int MindMapCount { get; set; }
}
