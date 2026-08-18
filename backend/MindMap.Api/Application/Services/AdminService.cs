using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Admin;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public class AdminService : IAdminService
{
    private readonly AppDbContext _db;

    public AdminService(AppDbContext db)
    {
        _db = db;
    }

    // ===================== 用户管理 =====================

    public async Task<PagedResult<AdminUserListItemDto>> GetUsersAsync(AdminUserListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "all" : query.Scope.ToLowerInvariant();

        IQueryable<User> q = _db.Users;

        if (scope == "active") q = q.Where(u => u.Status == UserStatus.Active);
        else if (scope == "disabled") q = q.Where(u => u.Status == UserStatus.Disabled);
        else if (scope == "admin") q = q.Where(u => u.IsAdmin);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(u => u.Username.Contains(kw) || u.Email.Contains(kw));
        }

        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserListItemDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                Avatar = u.Avatar,
                IsAdmin = u.IsAdmin,
                Status = (int)u.Status,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt,
                MindMapCount = u.MindMaps.Count
            })
            .ToListAsync(ct);

        return PagedResult<AdminUserListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task UpdateUserAsync(Guid targetUserId, AdminUserUpdateRequest req, Guid operatorUserId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
            ?? throw ApiException.NotFound("User", targetUserId);

        // 不能撤销自己的管理员权限，避免最后一个管理员失权
        if (targetUserId == operatorUserId && req.IsAdmin == false)
            throw ApiException.Conflict("不能撤销自己的管理员权限");

        if (req.IsAdmin.HasValue) user.IsAdmin = req.IsAdmin.Value;
        if (req.Status.HasValue)
        {
            // 禁用/启用前校验：不能禁用自己
            if (targetUserId == operatorUserId && req.Status.Value == (int)UserStatus.Disabled)
                throw ApiException.Conflict("不能禁用自己的账号");

            user.Status = (UserStatus)req.Status.Value;

            // 被禁用账号：吊销所有未过期的 refresh token
            if (user.Status == UserStatus.Disabled)
            {
                var now = DateTime.UtcNow;
                var activeTokens = await _db.RefreshTokens
                    .Where(t => t.UserId == user.Id && t.RevokedAt == null && t.ExpiresAt > now)
                    .ToListAsync(ct);
                foreach (var t in activeTokens) t.RevokedAt = now;
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteUserAsync(Guid targetUserId, Guid operatorUserId, CancellationToken ct = default)
    {
        if (targetUserId == operatorUserId)
            throw ApiException.Conflict("不能删除自己");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
            ?? throw ApiException.NotFound("User", targetUserId);

        // 级联删除由 EF 配置的 OnDelete(Cascade) 处理：导图、节点、版本、分享、举报、刷新令牌等
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
    }

    // ===================== 导图管理 =====================

    public async Task<PagedResult<AdminMindMapListItemDto>> GetMindMapsAsync(AdminMindMapListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "all" : query.Scope.ToLowerInvariant();

        IQueryable<MindMapEntity> q = _db.MindMaps;

        if (scope == "public") q = q.Where(m => m.IsPublic && !m.IsTakenDown);
        else if (scope == "takendown") q = q.Where(m => m.IsTakenDown);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(m => m.Title.Contains(kw) || (m.Description != null && m.Description.Contains(kw)));
        }

        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderByDescending(m => m.LastEditedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new AdminMindMapListItemDto
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                IsPublic = m.IsPublic,
                IsTakenDown = m.IsTakenDown,
                TakenDownReason = m.TakenDownReason,
                TakenDownAt = m.TakenDownAt,
                NodeCount = m.NodeCount,
                CreatedAt = m.CreatedAt,
                LastEditedAt = m.LastEditedAt,
                OwnerId = m.OwnerId,
                OwnerName = m.Owner.Username
            })
            .ToListAsync(ct);

        return PagedResult<AdminMindMapListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task TakeDownMindMapAsync(Guid mindMapId, AdminMindMapTakeDownRequest req, Guid operatorUserId, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct)
            ?? throw ApiException.NotFound("MindMap", mindMapId);

        if (map.IsTakenDown)
            throw ApiException.Conflict("该导图已被下架");

        map.IsTakenDown = true;
        map.TakenDownAt = DateTime.UtcNow;
        map.TakenDownReason = string.IsNullOrWhiteSpace(req.Reason) ? "管理员下架" : req.Reason.Trim();
        map.UpdatedAt = DateTime.UtcNow;

        // 同时禁用该导图下所有有效分享
        var shares = await _db.MindMapShares
            .Where(s => s.MindMapId == mindMapId && !s.IsDisabled)
            .ToListAsync(ct);
        foreach (var s in shares) s.IsDisabled = true;

        await _db.SaveChangesAsync(ct);
    }

    public async Task RestoreMindMapAsync(Guid mindMapId, Guid operatorUserId, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct)
            ?? throw ApiException.NotFound("MindMap", mindMapId);

        if (!map.IsTakenDown)
            throw ApiException.Conflict("该导图未被下架");

        map.IsTakenDown = false;
        map.TakenDownAt = null;
        map.TakenDownReason = null;
        map.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteMindMapAsync(Guid mindMapId, Guid operatorUserId, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct)
            ?? throw ApiException.NotFound("MindMap", mindMapId);

        _db.MindMaps.Remove(map);
        await _db.SaveChangesAsync(ct);
    }

    // ===================== 统计 =====================

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7).Date;

        var stats = new AdminStatsDto
        {
            UserCount = await _db.Users.LongCountAsync(ct),
            ActiveUserCount = await _db.Users.LongCountAsync(u => u.Status == UserStatus.Active, ct),
            DisabledUserCount = await _db.Users.LongCountAsync(u => u.Status == UserStatus.Disabled, ct),
            AdminCount = await _db.Users.LongCountAsync(u => u.IsAdmin, ct),

            MindMapCount = await _db.MindMaps.LongCountAsync(ct),
            PublicMindMapCount = await _db.MindMaps.LongCountAsync(m => m.IsPublic && !m.IsTakenDown, ct),
            TakenDownMindMapCount = await _db.MindMaps.LongCountAsync(m => m.IsTakenDown, ct),

            ShareCount = await _db.MindMapShares.LongCountAsync(ct),
            ActiveShareCount = await _db.MindMapShares.LongCountAsync(s => !s.IsDisabled && (s.ExpiresAt == null || s.ExpiresAt > now), ct),

            PendingReportCount = await _db.MindMapReports.LongCountAsync(r => r.Status == (int)ReportStatus.Pending, ct),
            TotalReportCount = await _db.MindMapReports.LongCountAsync(ct)
        };

        stats.NewUsersLast7Days = await GetDailyUserCounts(sevenDaysAgo, now, ct);
        stats.NewMindMapsLast7Days = await GetDailyMindMapCounts(sevenDaysAgo, now, ct);

        return stats;
    }

    private async Task<List<AdminDailyCount>> GetDailyUserCounts(DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var raw = await _db.Users
            .Where(u => u.CreatedAt >= startDate)
            .Select(u => u.CreatedAt)
            .ToListAsync(ct);

        return BuildDailyCounts(raw, startDate, endDate);
    }

    private async Task<List<AdminDailyCount>> GetDailyMindMapCounts(DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        var raw = await _db.MindMaps
            .Where(m => m.CreatedAt >= startDate)
            .Select(m => m.CreatedAt)
            .ToListAsync(ct);

        return BuildDailyCounts(raw, startDate, endDate);
    }

    private static List<AdminDailyCount> BuildDailyCounts(List<DateTime> raw, DateTime startDate, DateTime endDate)
    {
        var lookup = raw
            .Select(d => d.ToString("yyyy-MM-dd"))
            .GroupBy(s => s)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var result = new List<AdminDailyCount>();
        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd");
            result.Add(new AdminDailyCount { Date = key, Count = lookup.TryGetValue(key, out var c) ? c : 0 });
        }
        return result;
    }

    // ===================== 举报 =====================

    public async Task<PagedResult<AdminReportListItemDto>> GetReportsAsync(AdminReportListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "pending" : query.Scope.ToLowerInvariant();

        IQueryable<MindMapReport> q = _db.MindMapReports;

        if (scope == "pending") q = q.Where(r => r.Status == (int)ReportStatus.Pending);
        else if (scope == "resolved") q = q.Where(r => r.Status != (int)ReportStatus.Pending);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(r => r.Reason.Contains(kw) || r.MindMap.Title.Contains(kw));
        }

        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminReportListItemDto
            {
                Id = r.Id,
                MindMapId = r.MindMapId,
                MindMapTitle = r.MindMap.Title,
                MindMapOwnerId = r.MindMap.OwnerId,
                MindMapOwnerName = r.MindMap.Owner.Username,
                ReporterId = r.ReporterId,
                ReporterName = r.Reporter != null ? r.Reporter.Username : null,
                Reason = r.Reason,
                Status = r.Status,
                ResolutionNote = r.ResolutionNote,
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt
            })
            .ToListAsync(ct);

        return PagedResult<AdminReportListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task<AdminReportListItemDto> CreateReportAsync(Guid? reporterId, Guid mindMapId, AdminReportCreateRequest req, CancellationToken ct = default)
    {
        var mapExists = await _db.MindMaps.AnyAsync(m => m.Id == mindMapId, ct);
        if (!mapExists) throw ApiException.NotFound("MindMap", mindMapId);

        var report = new MindMapReport
        {
            Id = Guid.NewGuid(),
            MindMapId = mindMapId,
            ReporterId = reporterId,
            Reason = req.Reason.Trim(),
            Status = (int)ReportStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        _db.MindMapReports.Add(report);
        await _db.SaveChangesAsync(ct);

        // 重新加载关联数据返回
        var dto = await _db.MindMapReports
            .Where(r => r.Id == report.Id)
            .Select(r => new AdminReportListItemDto
            {
                Id = r.Id,
                MindMapId = r.MindMapId,
                MindMapTitle = r.MindMap.Title,
                MindMapOwnerId = r.MindMap.OwnerId,
                MindMapOwnerName = r.MindMap.Owner.Username,
                ReporterId = r.ReporterId,
                ReporterName = r.Reporter != null ? r.Reporter.Username : null,
                Reason = r.Reason,
                Status = r.Status,
                ResolutionNote = r.ResolutionNote,
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt
            })
            .FirstAsync(ct);

        return dto;
    }

    public async Task ResolveReportAsync(Guid reportId, AdminReportResolveRequest req, Guid operatorUserId, CancellationToken ct = default)
    {
        var report = await _db.MindMapReports
            .Include(r => r.MindMap)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw ApiException.NotFound("Report", reportId);

        if (report.Status != (int)ReportStatus.Pending)
            throw ApiException.Conflict("该举报已被处理");

        report.Status = req.TakeDown ? (int)ReportStatus.TakenDown : (int)ReportStatus.Rejected;
        report.ResolutionNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        report.ResolvedById = operatorUserId;
        report.ResolvedAt = DateTime.UtcNow;

        if (req.TakeDown && !report.MindMap.IsTakenDown)
        {
            report.MindMap.IsTakenDown = true;
            report.MindMap.TakenDownAt = DateTime.UtcNow;
            report.MindMap.TakenDownReason = $"举报处理下架：{report.Reason}";
            report.MindMap.UpdatedAt = DateTime.UtcNow;

            // 同步禁用该导图下所有分享
            var shares = await _db.MindMapShares
                .Where(s => s.MindMapId == report.MindMapId && !s.IsDisabled)
                .ToListAsync(ct);
            foreach (var s in shares) s.IsDisabled = true;
        }

        await _db.SaveChangesAsync(ct);
    }
}
