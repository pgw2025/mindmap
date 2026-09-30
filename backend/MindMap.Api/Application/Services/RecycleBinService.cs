using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindMap.Api.Application.DTOs.RecycleBin;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Options;
using MindMap.Api.Common.Responses;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IRecycleBinService
{
    Task<PagedResult<RecycleBinItemDto>> GetListAsync(Guid userId, RecycleBinListQuery query, CancellationToken ct = default);
    Task RestoreAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<RecycleBinSettingsDto> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task<RecycleBinSettingsDto> UpdateSettingsAsync(Guid userId, RecycleBinSettingsUpdateRequest req, CancellationToken ct = default);
}

/// <summary>
/// 回收站：软删除导图的列表、恢复、彻底删除与保留天数设置。
/// 读取软删数据统一走 IgnoreQueryFilters（MindMap 配有 DeletedAt == null 全局过滤器）。
/// </summary>
public class RecycleBinService : IRecycleBinService
{
    private readonly AppDbContext _db;
    private readonly RecycleBinOptions _options;
    private readonly IMindMapService _mindMaps;

    public RecycleBinService(AppDbContext db, IOptions<RecycleBinOptions> options, IMindMapService mindMaps)
    {
        _db = db;
        _options = options.Value;
        _mindMaps = mindMaps;
    }

    public async Task<PagedResult<RecycleBinItemDto>> GetListAsync(Guid userId, RecycleBinListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var retentionDays = await GetRetentionDaysAsync(userId, ct);

        IQueryable<MindMapEntity> q = _db.MindMaps
            .IgnoreQueryFilters()
            .Where(m => m.OwnerId == userId && m.DeletedAt != null);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(m => m.Title.Contains(kw) || (m.Description != null && m.Description.Contains(kw)));
        }

        var total = await q.LongCountAsync(ct);

        var now = DateTime.UtcNow;
        var items = await q
            .OrderByDescending(m => m.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new RecycleBinItemDto
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                NodeCount = m.Nodes.Count,
                DeletedAt = m.DeletedAt!.Value,
                ExpiresAt = m.DeletedAt!.Value.AddDays(retentionDays),
                DaysLeft = EF.Functions.DateDiffDay(now, m.DeletedAt!.Value.AddDays(retentionDays)),
                FolderId = m.FolderId,
                FolderName = m.Folder != null ? m.Folder.Name : null
            })
            .ToListAsync(ct);

        return PagedResult<RecycleBinItemDto>.Create(items, total, page, pageSize);
    }

    /// <summary>
    /// 恢复导图。用条件 ExecuteUpdate 保证与过期清理并发时只有一个赢家：
    /// 清理服务若已物理删除，此处 0 行受影响返回 404。
    /// 保留原 LastEditedAt —— 恢复后导图回到列表原位置。
    /// </summary>
    public async Task RestoreAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var affected = await _db.MindMaps
            .IgnoreQueryFilters()
            .Where(m => m.Id == id && m.OwnerId == userId && m.DeletedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.DeletedAt, (DateTime?)null)
                .SetProperty(m => m.UpdatedAt, DateTime.UtcNow), ct);

        if (affected == 0) throw ApiException.NotFound("MindMap", id);
    }

    /// <summary>彻底删除（物理清除，不可恢复）。</summary>
    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var exists = await _db.MindMaps
            .IgnoreQueryFilters()
            .AnyAsync(m => m.Id == id && m.OwnerId == userId && m.DeletedAt != null, ct);
        if (!exists) throw ApiException.NotFound("MindMap", id);

        await _mindMaps.HardDeleteAsync(id, ct);
    }

    public async Task<RecycleBinSettingsDto> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var retentionDays = await GetRetentionDaysAsync(userId, ct);
        var custom = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.RecycleBinRetentionDays)
            .FirstOrDefaultAsync(ct);

        return new RecycleBinSettingsDto
        {
            EffectiveRetentionDays = retentionDays,
            CustomRetentionDays = custom
        };
    }

    public async Task<RecycleBinSettingsDto> UpdateSettingsAsync(Guid userId, RecycleBinSettingsUpdateRequest req, CancellationToken ct = default)
    {
        if (req.RetentionDays is < 1 or > 365)
            throw ApiException.BadRequest("保留天数须在 1–365 之间");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("User", userId);

        user.RecycleBinRetentionDays = req.RetentionDays;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new RecycleBinSettingsDto
        {
            EffectiveRetentionDays = req.RetentionDays ?? _options.DefaultRetentionDays,
            CustomRetentionDays = req.RetentionDays
        };
    }

    /// <summary>当前生效保留天数：个人覆盖优先，否则全局默认。</summary>
    private async Task<int> GetRetentionDaysAsync(Guid userId, CancellationToken ct = default)
    {
        var custom = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.RecycleBinRetentionDays)
            .FirstOrDefaultAsync(ct);

        return RecycleBinRetention.GetEffectiveDays(custom, _options.DefaultRetentionDays);
    }
}

/// <summary>
/// 保留天数解算（回收站服务与过期清理服务共用）。
/// </summary>
public static class RecycleBinRetention
{
    public static int GetEffectiveDays(int? userDays, int defaultDays)
        => userDays is >= 1 and <= 365 ? userDays.Value
        : (userDays is null && defaultDays >= 1 ? defaultDays : 30);
}
