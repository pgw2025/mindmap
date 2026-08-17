using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Versions;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IMindMapVersionService
{
    Task<List<MindMapVersionDto>> ListAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default);
    Task<MindMapVersionDto> CreateAsync(Guid userId, Guid mindMapId, CreateVersionRequest req, CancellationToken ct = default);
    Task RollbackAsync(Guid userId, Guid mindMapId, Guid versionId, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid mindMapId, Guid versionId, CancellationToken ct = default);
}

public class MindMapVersionService : IMindMapVersionService
{
    private readonly AppDbContext _db;

    public MindMapVersionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MindMapVersionDto>> ListAsync(Guid? userId, Guid mindMapId, CancellationToken ct)
    {
        var map = await _db.MindMaps.AsNoTracking()
            .Select(m => new { m.Id, m.OwnerId, m.IsPublic })
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId && !map.IsPublic) throw ApiException.Forbidden("无权访问");

        return await _db.MindMapVersions.AsNoTracking()
            .Where(v => v.MindMapId == mindMapId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new MindMapVersionDto(
                v.Id,
                v.VersionNumber,
                v.Remark,
                v.NodeCount,
                v.CreatedById,
                v.CreatedBy.Username,
                v.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<MindMapVersionDto> CreateAsync(Guid userId, Guid mindMapId, CreateVersionRequest req, CancellationToken ct)
    {
        var map = await _db.MindMaps
            .Include(m => m.Nodes)
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId) throw ApiException.Forbidden("无权操作");

        // 序列化节点树
        var nodes = map.Nodes.ToList();
        var tree = BuildTree(nodes);
        var json = JsonSerializer.Serialize(tree);

        // 计算下一版本号
        var nextVersion = await _db.MindMapVersions
            .Where(v => v.MindMapId == mindMapId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
        nextVersion++;

        var version = new MindMapVersion
        {
            Id = Guid.NewGuid(),
            MindMapId = mindMapId,
            VersionNumber = nextVersion,
            Remark = req.Remark?.Trim(),
            NodeSnapshotJson = json,
            NodeCount = nodes.Count,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.MindMapVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstAsync(ct);

        return new MindMapVersionDto(
            version.Id,
            version.VersionNumber,
            version.Remark,
            version.NodeCount,
            userId,
            user,
            version.CreatedAt
        );
    }

    public async Task RollbackAsync(Guid userId, Guid mindMapId, Guid versionId, CancellationToken ct)
    {
        var map = await _db.MindMaps
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId) throw ApiException.Forbidden("无权操作");

        var version = await _db.MindMapVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.MindMapId == mindMapId, ct);
        if (version is null) throw ApiException.NotFound("版本", versionId);
        var tree = JsonSerializer.Deserialize<List<VersionNodeData>>(version.NodeSnapshotJson);
        if (tree is null || tree.Count == 0) throw new ApiException("版本数据为空", StatusCodes.Status400BadRequest);

        // 删除所有现有节点
        var allNodes = await _db.Nodes
            .Where(n => n.MindMapId == mindMapId)
            .Select(n => n.Id)
            .ToListAsync(ct);

        // 清空 RootNodeId，避免 RESTRICT 删除失败
        // 注意：必须先 SaveChangesAsync 持久化到数据库，
        // 因为后续 ExecuteUpdateAsync / ExecuteDeleteAsync 绕过 ChangeTracker，
        // 数据库中 mindmaps.RootNodeId 仍指向旧节点 Id 会触发 ON DELETE RESTRICT。
        map.RootNodeId = null;
        await _db.SaveChangesAsync(ct);

        if (allNodes.Count > 0)
        {
            // 自引用表需分批：先查叶子再删，或直接按层级 BFS 删除。
            // 简单做法：先删除所有非叶节点（没有子节点引用它们的），这里直接用 BFS 反向。
            // 更稳妥：先清空 ParentId 解除引用，再批量删除。
            await _db.Nodes.Where(n => allNodes.Contains(n.Id))
                .ExecuteUpdateAsync(setter => setter.SetProperty(n => n.ParentId, (Guid?)null), ct);
            await _db.Nodes.Where(n => allNodes.Contains(n.Id))
                .ExecuteDeleteAsync(ct);
        }

        // 重新插入节点
        var nodeMap = new Dictionary<Guid, Node>();
        void RecursiveInsert(List<VersionNodeData> items, Guid? parentId, int depth)
        {
            foreach (var item in items)
            {
                var node = new Node
                {
                    Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
                    MindMapId = mindMapId,
                    ParentId = parentId,
                    Title = item.Title ?? string.Empty,
                    Content = item.Content,
                    Note = item.Note,
                    SortOrder = item.SortOrder,
                    IsCollapsed = item.IsCollapsed,
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    Color = item.Color,
                    FontSize = item.FontSize,
                    FontFamily = item.FontFamily,
                    Icon = item.Icon,
                    BorderColor = item.BorderColor,
                    BackgroundColor = item.BackgroundColor,
                    EdgeColor = item.EdgeColor,
                    Shape = (Domain.Entities.Enums.NodeShape?)item.Shape,
                    EdgeStyle = (Domain.Entities.Enums.EdgeStyle?)item.EdgeStyle,
                    ExtraData = item.ExtraData,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.Nodes.Add(node);
                nodeMap[node.Id] = node;

                if (depth == 0 && parentId is null)
                {
                    map.RootNodeId = node.Id;
                }

                if (item.Children != null && item.Children.Count > 0)
                {
                    RecursiveInsert(item.Children, node.Id, depth + 1);
                }
            }
        }

        RecursiveInsert(tree, null, 0);
        map.NodeCount = nodeMap.Count;
        map.UpdatedAt = DateTime.UtcNow;
        map.LastEditedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid mindMapId, Guid versionId, CancellationToken ct)
    {
        var map = await _db.MindMaps.AsNoTracking()
            .Select(m => new { m.Id, m.OwnerId })
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId) throw ApiException.Forbidden("无权操作");

        var version = await _db.MindMapVersions
            .FirstOrDefaultAsync(v => v.Id == versionId && v.MindMapId == mindMapId, ct);
        if (version is null) throw ApiException.NotFound("版本", versionId);

        _db.MindMapVersions.Remove(version);
        await _db.SaveChangesAsync(ct);
    }

    private static List<VersionNodeData> BuildTree(List<Node> nodes)
    {
        var byParent = nodes.ToLookup(n => n.ParentId);
        var rootNodes = byParent[(Guid?)null].OrderBy(n => n.SortOrder).ToList();
        var tree = new List<VersionNodeData>(rootNodes.Count);
        foreach (var root in rootNodes)
        {
            tree.Add(BuildNode(root, byParent));
        }
        return tree;
    }

    private static VersionNodeData BuildNode(Node n, ILookup<Guid?, Node> byParent)
    {
        var children = byParent[n.Id].OrderBy(c => c.SortOrder).ToList();
        return new VersionNodeData(
            n.Id,
            n.ParentId,
            n.Title,
            n.Content,
            n.Note,
            n.SortOrder,
            n.IsCollapsed,
            n.X,
            n.Y,
            n.Width,
            n.Height,
            n.Color,
            n.FontSize,
            n.FontFamily,
            n.Icon,
            n.BorderColor,
            n.BackgroundColor,
            n.EdgeColor,
            (int?)n.Shape,
            (int?)n.EdgeStyle,
            n.ExtraData,
            children.Count > 0 ? children.Select(c => BuildNode(c, byParent)).ToList() : null
        );
    }
}
