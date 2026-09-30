using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.RecycleBin;

public class RecycleBinListQuery
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RecycleBinItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int NodeCount { get; set; }

    /// <summary>移入回收站时间（UTC）。</summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>预计物理清除时间（UTC）= DeletedAt + 当前生效保留天数。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>剩余保留天数（向下取整，0 表示今天到期）。</summary>
    public int DaysLeft { get; set; }

    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
}

public class RecycleBinSettingsDto
{
    /// <summary>当前生效的保留天数（个人覆盖或全局默认）。</summary>
    public int EffectiveRetentionDays { get; set; }

    /// <summary>个人覆盖天数；null = 跟随全局默认。</summary>
    public int? CustomRetentionDays { get; set; }
}

public class RecycleBinSettingsUpdateRequest
{
    /// <summary>个人保留天数；null = 恢复跟随全局默认。范围 1–365。</summary>
    [Range(1, 365)]
    public int? RetentionDays { get; set; }
}
