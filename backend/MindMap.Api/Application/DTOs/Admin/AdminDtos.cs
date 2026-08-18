using System.ComponentModel.DataAnnotations;

namespace MindMap.Api.Application.DTOs.Admin;

// ===================== 通用查询 =====================

public class AdminPagedQuery
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ===================== 用户管理 =====================

public class AdminUserListItemDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsAdmin { get; set; }
    public int Status { get; set; } // 0=Active 1=Disabled
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public int MindMapCount { get; set; }
}

public class AdminUserListQuery : AdminPagedQuery
{
    /// <summary>"all" | "active" | "disabled" | "admin"</summary>
    public string Scope { get; set; } = "all";
}

public class AdminUserUpdateRequest
{
    public bool? IsAdmin { get; set; }
    /// <summary>0=Active 1=Disabled</summary>
    public int? Status { get; set; }
}

// ===================== 导图管理 =====================

public class AdminMindMapListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public bool IsTakenDown { get; set; }
    public string? TakenDownReason { get; set; }
    public DateTime? TakenDownAt { get; set; }
    public int NodeCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastEditedAt { get; set; }

    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
}

public class AdminMindMapListQuery : AdminPagedQuery
{
    /// <summary>"all" | "public" | "takenDown"</summary>
    public string Scope { get; set; } = "all";
}

public class AdminMindMapTakeDownRequest
{
    [StringLength(256)]
    public string? Reason { get; set; }
}

// ===================== 统计看板 =====================

public class AdminStatsDto
{
    public long UserCount { get; set; }
    public long ActiveUserCount { get; set; }
    public long DisabledUserCount { get; set; }
    public long AdminCount { get; set; }

    public long MindMapCount { get; set; }
    public long PublicMindMapCount { get; set; }
    public long TakenDownMindMapCount { get; set; }

    public long ShareCount { get; set; }
    public long ActiveShareCount { get; set; }

    public long PendingReportCount { get; set; }
    public long TotalReportCount { get; set; }

    /// <summary>近 7 日每日新增用户数（按 UTC 日期分组，最早到最近）。</summary>
    public List<AdminDailyCount> NewUsersLast7Days { get; set; } = new();
    /// <summary>近 7 日每日新增导图数。</summary>
    public List<AdminDailyCount> NewMindMapsLast7Days { get; set; } = new();
}

public class AdminDailyCount
{
    public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public long Count { get; set; }
}

// ===================== 举报管理 =====================

/// <summary>举报状态：0=待处理 1=已驳回 2=已下架</summary>
public enum ReportStatus
{
    Pending = 0,
    Rejected = 1,
    TakenDown = 2
}

public class AdminReportListItemDto
{
    public Guid Id { get; set; }
    public Guid MindMapId { get; set; }
    public string MindMapTitle { get; set; } = string.Empty;
    public Guid MindMapOwnerId { get; set; }
    public string MindMapOwnerName { get; set; } = string.Empty;

    public Guid? ReporterId { get; set; }
    public string? ReporterName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class AdminReportListQuery : AdminPagedQuery
{
    /// <summary>"pending" | "resolved" | "all"</summary>
    public string Scope { get; set; } = "pending";
}

public class AdminReportCreateRequest
{
    [Required, StringLength(512, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public class AdminReportResolveRequest
{
    /// <summary>true=下架导图并标记举报已处理；false=驳回举报</summary>
    public bool TakeDown { get; set; }

    [StringLength(512)]
    public string? Note { get; set; }
}
