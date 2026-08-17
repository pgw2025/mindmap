namespace MindMap.Api.Domain.Entities;

/// <summary>
/// 用户私有的文件夹（树形结构）。ParentId 为 null 表示根级。
/// </summary>
public class Folder
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MindMapEntity> MindMaps { get; set; } = new List<MindMapEntity>();
}
