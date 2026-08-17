using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Tags;

public class TagCreateRequest
{
    [Required, StringLength(32, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(16)]
    public string Color { get; set; } = "#18a058";
}

public class TagUpdateRequest
{
    [StringLength(32, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(16)]
    public string? Color { get; set; }
}

public class TagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#18a058";
    public DateTime CreatedAt { get; set; }
    public int MindMapCount { get; set; }
}
