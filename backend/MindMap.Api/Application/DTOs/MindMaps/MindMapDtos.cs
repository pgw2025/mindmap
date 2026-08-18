using System.ComponentModel.DataAnnotations;
using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Application.DTOs.MindMaps;

public class MindMapCreateRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? Description { get; set; }

    public Guid? FolderId { get; set; }

    public bool IsPublic { get; set; }

    public MindMapLayout DefaultLayout { get; set; } = MindMapLayout.Left;

    [StringLength(64)]
    public string? Theme { get; set; }

    public List<Guid> TagIds { get; set; } = new();
}

public class MindMapUpdateRequest
{
    [StringLength(128, MinimumLength = 1)]
    public string? Title { get; set; }

    [StringLength(2048)]
    public string? Description { get; set; }

    public Guid? FolderId { get; set; }

    public bool? IsPublic { get; set; }

    public MindMapLayout? DefaultLayout { get; set; }

    [StringLength(64)]
    public string? Theme { get; set; }
}

public class MindMapListQuery
{
    /// <summary>"mine" | "public"，默认 mine</summary>
    public string Scope { get; set; } = "mine";

    public Guid? FolderId { get; set; }
    public Guid? TagId { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class MindMapTagsRequest
{
    public List<Guid> TagIds { get; set; } = new();
}

public class MindMapCopyRequest
{
    [StringLength(128, MinimumLength = 1)]
    public string? NewTitle { get; set; }
}

public class TagBriefDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#18a058";
}

public class MindMapListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public string? CoverImage { get; set; }
    public int DefaultLayout { get; set; }
    public int NodeCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastEditedAt { get; set; }

    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }

    public List<TagBriefDto> Tags { get; set; } = new();
}

public class MindMapDetailDto : MindMapListItemDto
{
    public DateTime UpdatedAt { get; set; }
    public string? Theme { get; set; }
    public Guid? RootNodeId { get; set; }
}
