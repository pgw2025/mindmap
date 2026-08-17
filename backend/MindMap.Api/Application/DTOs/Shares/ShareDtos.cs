using System.ComponentModel.DataAnnotations;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Application.DTOs.Nodes;

namespace MindMap.Api.Application.DTOs.Shares;

public class ShareCreateRequest
{
    public bool? SetPublic { get; set; }

    [StringLength(32)]
    public string? Password { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int? MaxAccessCount { get; set; }

    public bool AllowCopy { get; set; } = true;
}

public class ShareDto
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public string? Password { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxAccessCount { get; set; }
    public int AccessCount { get; set; }
    public bool AllowCopy { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}

public class ShareListDto
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public bool HasPassword { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxAccessCount { get; set; }
    public int AccessCount { get; set; }
    public bool AllowCopy { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}

public class ShareVerifyRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public string? Password { get; set; }
}

public class ShareVerifyResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool NeedsPassword { get; set; }
    public Guid? MindMapId { get; set; }
    public string? Title { get; set; }
    public Guid? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public bool AllowCopy { get; set; }
    public string? AccessToken { get; set; }
}

public class ShareMindMapResponse
{
    public MindMapDetailDto MindMap { get; set; } = null!;
    public List<NodeTreeNodeDto> Nodes { get; set; } = new();
}
