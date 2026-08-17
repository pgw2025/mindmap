namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 标签。用户私有，可挂到多个导图。
/// </summary>
public class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#18a058";

    public DateTime CreatedAt { get; set; }

    public ICollection<MindMap> MindMaps { get; set; } = new List<MindMap>();
}
