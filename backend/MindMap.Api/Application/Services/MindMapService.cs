using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IMindMapService
{
    Task<PagedResult<MindMapListItemDto>> GetListAsync(Guid? userId, MindMapListQuery query, CancellationToken ct = default);
    Task<MindMapDetailDto?> GetAsync(Guid? userId, Guid id, CancellationToken ct = default);
    Task<MindMapDetailDto> CreateAsync(Guid userId, MindMapCreateRequest req, CancellationToken ct = default);
    Task<MindMapDetailDto> UpdateAsync(Guid userId, Guid id, MindMapUpdateRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<MindMapDetailDto> CopyAsync(Guid userId, Guid id, MindMapCopyRequest req, CancellationToken ct = default);
    Task SetTagsAsync(Guid userId, Guid id, List<Guid> tagIds, CancellationToken ct = default);
}

public class MindMapService : IMindMapService
{
    private readonly AppDbContext _db;

    public MindMapService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<MindMapListItemDto>> GetListAsync(Guid? userId, MindMapListQuery query, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "mine" : query.Scope.ToLowerInvariant();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        IQueryable<MindMapEntity> q = _db.MindMaps;

        if (scope == "mine")
        {
            if (userId is null) throw ApiException.Forbidden("未登录");
            q = q.Where(m => m.OwnerId == userId.Value);
        }
        else // public
        {
            // 下架的导图不出现在公开广场（导图所有者仍可在"我的导图"看到）
            q = q.Where(m => m.IsPublic && !m.IsTakenDown);
        }

        if (query.FolderId.HasValue && scope == "mine")
        {
            q = q.Where(m => m.FolderId == query.FolderId.Value);
        }

        if (query.TagId.HasValue)
        {
            q = q.Where(m => m.Tags.Any(t => t.Id == query.TagId.Value));
        }

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
            .Select(m => new MindMapListItemDto
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                IsPublic = m.IsPublic,
                CoverImage = m.CoverImage,
                DefaultLayout = (int)m.DefaultLayout,
                NodeCount = m.NodeCount,
                CreatedAt = m.CreatedAt,
                LastEditedAt = m.LastEditedAt,
                OwnerId = m.OwnerId,
                OwnerName = m.Owner.Username,
                FolderId = m.FolderId,
                FolderName = m.Folder != null ? m.Folder.Name : null,
                Tags = m.Tags.Select(t => new TagBriefDto { Id = t.Id, Name = t.Name, Color = t.Color }).ToList()
            })
            .ToListAsync(ct);

        return PagedResult<MindMapListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task<MindMapDetailDto?> GetAsync(Guid? userId, Guid id, CancellationToken ct = default)
    {
        // 所有者可访问自己的导图（即使被下架）；公开导图需未被下架
        var dto = await _db.MindMaps
            .Where(m => m.Id == id && (
                (userId.HasValue && m.OwnerId == userId.Value) ||
                (m.IsPublic && !m.IsTakenDown)))
            .Select(m => new MindMapDetailDto
            {
                Id = m.Id,
                Title = m.Title,
                Description = m.Description,
                IsPublic = m.IsPublic,
                CoverImage = m.CoverImage,
                DefaultLayout = (int)m.DefaultLayout,
                NodeCount = m.NodeCount,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                LastEditedAt = m.LastEditedAt,
                Theme = m.Theme,
                RootNodeId = m.RootNodeId,
                OwnerId = m.OwnerId,
                OwnerName = m.Owner.Username,
                FolderId = m.FolderId,
                FolderName = m.Folder != null ? m.Folder.Name : null,
                Tags = m.Tags.Select(t => new TagBriefDto { Id = t.Id, Name = t.Name, Color = t.Color }).ToList()
            })
            .FirstOrDefaultAsync(ct);
        return dto;
    }

    public async Task<MindMapDetailDto> CreateAsync(Guid userId, MindMapCreateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.FolderId.HasValue)
        {
            var folderOwned = await _db.Folders.AnyAsync(f => f.Id == req.FolderId.Value && f.UserId == userId, ct);
            if (!folderOwned) throw ApiException.NotFound("Folder", req.FolderId.Value);
        }

        var validTagIds = await FilterOwnedTagIdsAsync(userId, req.TagIds, ct);

        var map = new MindMapEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            FolderId = req.FolderId,
            Title = req.Title,
            Description = req.Description,
            IsPublic = req.IsPublic,
            DefaultLayout = req.DefaultLayout,
            Theme = req.Theme,
            NodeCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
        };

        if (validTagIds.Count > 0)
        {
            var tags = await _db.Tags.Where(t => validTagIds.Contains(t.Id)).ToListAsync(ct);
            foreach (var t in tags) map.Tags.Add(t);
        }

        _db.MindMaps.Add(map);
        await _db.SaveChangesAsync(ct);

        return (await GetAsync(userId, map.Id, ct))!;
    }

    public async Task<MindMapDetailDto> UpdateAsync(Guid userId, Guid id, MindMapUpdateRequest req, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == userId, ct)
            ?? throw ApiException.NotFound("MindMap", id);

        if (req.Title is not null) map.Title = req.Title;
        if (req.Description is not null) map.Description = req.Description;
        if (req.IsPublic.HasValue) map.IsPublic = req.IsPublic.Value;
        if (req.DefaultLayout.HasValue) map.DefaultLayout = req.DefaultLayout.Value;
        if (req.Theme is not null) map.Theme = req.Theme;

        if (req.FolderId.HasValue)
        {
            if (req.FolderId.Value != Guid.Empty)
            {
                var folderOwned = await _db.Folders.AnyAsync(f => f.Id == req.FolderId.Value && f.UserId == userId, ct);
                if (!folderOwned) throw ApiException.NotFound("Folder", req.FolderId.Value);
                map.FolderId = req.FolderId.Value;
            }
            else
            {
                // 0 表示移到根级
                map.FolderId = null;
            }
        }

        map.UpdatedAt = DateTime.UtcNow;
        map.LastEditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return (await GetAsync(userId, id, ct))!;
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == userId, ct)
            ?? throw ApiException.NotFound("MindMap", id);

        // 1. 先清空 RootNodeId 引用，否则后续删除根节点时会被
        //    MindMap.RootNodeId -> Node 的 Restrict FK 阻止。
        map.RootNodeId = null;
        await _db.SaveChangesAsync(ct);

        // 2. 解除节点间的父子自引用。
        //    Node.ParentId -> Node 的自引用 FK 为 Restrict，
        //    直接批量删除 MySQL 无法按拓扑顺序执行，必须先把 ParentId 置空。
        await _db.Nodes
            .Where(n => n.MindMapId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ParentId, (Guid?)null), ct);

        // 3. 批量删除该思维导图下的所有节点。
        await _db.Nodes
            .Where(n => n.MindMapId == id)
            .ExecuteDeleteAsync(ct);

        // 4. 删除思维导图本身（节点外键已清理，MindMap -> Nodes 级联不再触发）。
        _db.MindMaps.Remove(map);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<MindMapDetailDto> CopyAsync(Guid userId, Guid id, MindMapCopyRequest req, CancellationToken ct = default)
    {
        var src = await _db.MindMaps
            .Include(m => m.Tags)
            .FirstOrDefaultAsync(m => m.Id == id && (m.IsPublic || m.OwnerId == userId), ct);
        if (src is null) throw ApiException.NotFound("MindMap", id);

        var copy = new MindMapEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            FolderId = null,
            Title = string.IsNullOrWhiteSpace(req.NewTitle) ? $"{src.Title} 副本" : req.NewTitle,
            Description = src.Description,
            IsPublic = false,
            DefaultLayout = src.DefaultLayout,
            Theme = src.Theme,
            NodeCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastEditedAt = DateTime.UtcNow
            // 注意：阶段 4 加 Nodes 后，复制要一并复制节点结构
        };

        var tagIds = src.Tags.Select(t => t.Id).ToList();
        if (tagIds.Count > 0)
        {
            var tags = await _db.Tags.Where(t => tagIds.Contains(t.Id) && t.UserId == userId).ToListAsync(ct);
            foreach (var t in tags) copy.Tags.Add(t);
        }

        _db.MindMaps.Add(copy);
        await _db.SaveChangesAsync(ct);
        return (await GetAsync(userId, copy.Id, ct))!;
    }

    public async Task SetTagsAsync(Guid userId, Guid id, List<Guid> tagIds, CancellationToken ct = default)
    {
        var map = await _db.MindMaps.Include(m => m.Tags).FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == userId, ct)
            ?? throw ApiException.NotFound("MindMap", id);

        var validIds = await FilterOwnedTagIdsAsync(userId, tagIds, ct);
        var validTags = validIds.Count == 0
            ? new List<Tag>()
            : await _db.Tags.Where(t => validIds.Contains(t.Id)).ToListAsync(ct);

        map.Tags.Clear();
        foreach (var t in validTags) map.Tags.Add(t);
        map.UpdatedAt = DateTime.UtcNow;
        map.LastEditedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> FilterOwnedTagIdsAsync(Guid userId, List<Guid> tagIds, CancellationToken ct)
    {
        if (tagIds == null || tagIds.Count == 0) return new List<Guid>();
        var distinct = tagIds.Distinct().ToList();
        var owned = await _db.Tags
            .Where(t => t.UserId == userId && distinct.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);
        return owned;
    }
}
