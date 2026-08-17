using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Folders;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IFolderService
{
    Task<IReadOnlyList<FolderNodeDto>> GetTreeAsync(Guid userId, CancellationToken ct = default);
    Task<FolderDto> CreateAsync(Guid userId, FolderCreateRequest req, CancellationToken ct = default);
    Task<FolderDto> UpdateAsync(Guid userId, Guid id, FolderUpdateRequest req, CancellationToken ct = default);
    Task MoveAsync(Guid userId, Guid id, FolderMoveRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class FolderService : IFolderService
{
    private readonly AppDbContext _db;

    public FolderService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FolderNodeDto>> GetTreeAsync(Guid userId, CancellationToken ct = default)
    {
        var folders = await _db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.ParentId).ThenBy(f => f.SortOrder).ThenBy(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id, f.ParentId, f.Name, f.SortOrder, f.CreatedAt, f.UpdatedAt,
                MindMapCount = f.MindMaps.Count
            })
            .ToListAsync(ct);

        var map = folders.ToDictionary(
            f => f.Id,
            f => new FolderNodeDto
            {
                Id = f.Id, ParentId = f.ParentId, Name = f.Name, SortOrder = f.SortOrder,
                CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt, MindMapCount = f.MindMapCount
            });

        var roots = new List<FolderNodeDto>();
        foreach (var f in folders)
        {
            var node = map[f.Id];
            if (f.ParentId is null || !map.TryGetValue(f.ParentId.Value, out var parent))
            {
                roots.Add(node);
            }
            else
            {
                parent.Children.Add(node);
            }
        }
        return roots;
    }

    public async Task<FolderDto> CreateAsync(Guid userId, FolderCreateRequest req, CancellationToken ct = default)
    {
        if (req.ParentId.HasValue)
        {
            await EnsureOwnedAsync(userId, req.ParentId.Value, ct);
        }

        var next = await _db.Folders
            .Where(f => f.UserId == userId && f.ParentId == req.ParentId)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync(ct) ?? 0;

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentId = req.ParentId,
            Name = req.Name,
            SortOrder = req.SortOrder ?? next + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(ct);
        return ToDto(folder);
    }

    public async Task<FolderDto> UpdateAsync(Guid userId, Guid id, FolderUpdateRequest req, CancellationToken ct = default)
    {
        var folder = await EnsureOwnedAsync(userId, id, ct);
        folder.Name = req.Name;
        if (req.SortOrder.HasValue) folder.SortOrder = req.SortOrder.Value;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(folder);
    }

    public async Task MoveAsync(Guid userId, Guid id, FolderMoveRequest req, CancellationToken ct = default)
    {
        var folder = await EnsureOwnedAsync(userId, id, ct);

        if (req.ParentId == id)
            throw ApiException.Conflict("不能将文件夹移动到自身");

        if (req.ParentId.HasValue)
        {
            var parentId = req.ParentId.Value;
            await EnsureOwnedAsync(userId, parentId, ct);

            // 加载用户全部 (Id, ParentId) 映射，向上查祖先链，避免形成环
            var parentMap = await _db.Folders
                .Where(f => f.UserId == userId)
                .Select(f => new { f.Id, f.ParentId })
                .ToDictionaryAsync(f => f.Id, f => f.ParentId, ct);

            var ancestor = (Guid?)parentId;
            while (ancestor.HasValue)
            {
                if (ancestor.Value == id)
                    throw ApiException.Conflict("不能将文件夹移动到自己的子树下");
                ancestor = parentMap.TryGetValue(ancestor.Value, out var p) ? p : null;
            }
        }

        folder.ParentId = req.ParentId;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var folder = await EnsureOwnedAsync(userId, id, ct);

        // 不允许删除有子文件夹的节点
        var hasChildren = await _db.Folders.AnyAsync(f => f.ParentId == id, ct);
        if (hasChildren)
            throw ApiException.Conflict("该文件夹下还有子文件夹，请先删除子文件夹");

        // 不允许删除非空文件夹（保护用户数据）
        var hasMaps = await _db.MindMaps.AnyAsync(m => m.FolderId == id, ct);
        if (hasMaps)
            throw ApiException.Conflict("该文件夹下还有思维导图，请先移动或删除");

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Folder> EnsureOwnedAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct);
        return folder ?? throw ApiException.NotFound("Folder", id);
    }

    private static FolderDto ToDto(Folder f) => new()
    {
        Id = f.Id,
        ParentId = f.ParentId,
        Name = f.Name,
        SortOrder = f.SortOrder,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };
}
