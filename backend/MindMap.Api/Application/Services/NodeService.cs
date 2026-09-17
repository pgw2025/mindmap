using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Nodes;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface INodeService
{
    /// <summary>获取导图的所有节点（扁平列表，前端自行组装树）。</summary>
    Task<List<NodeDto>> GetByMindMapAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default);

    /// <summary>获取导图的节点树（递归）。</summary>
    Task<List<NodeTreeNodeDto>> GetTreeAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default);

    /// <summary>获取单个节点。</summary>
    Task<NodeDto?> GetAsync(Guid? userId, Guid id, CancellationToken ct = default);

    /// <summary>创建节点（如为根节点会同时更新 MindMap.RootNodeId）。</summary>
    Task<NodeDto> CreateAsync(Guid userId, Guid mindMapId, NodeCreateRequest req, CancellationToken ct = default);

    /// <summary>更新节点。</summary>
    Task<NodeDto> UpdateAsync(Guid userId, Guid id, NodeUpdateRequest req, CancellationToken ct = default);

    /// <summary>移动节点（变更父节点 + 排序）。</summary>
    Task<NodeDto> MoveAsync(Guid userId, Guid id, NodeMoveRequest req, CancellationToken ct = default);

    /// <summary>批量更新节点（拖拽排序场景）。</summary>
    Task BatchUpdateAsync(Guid userId, Guid mindMapId, NodeBatchUpdateRequest req, CancellationToken ct = default);

    /// <summary>删除节点（递归删除所有子孙节点；如为根节点会同时清除 MindMap.RootNodeId）。</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class NodeService : INodeService
{
    private readonly AppDbContext _db;

    public NodeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<NodeDto>> GetByMindMapAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(userId, mindMapId, ct);

        var list = await _db.Nodes
            .Where(n => n.MindMapId == mindMapId)
            .OrderBy(n => n.ParentId == null ? 0 : 1)
            .ThenBy(n => n.SortOrder)
            .ThenBy(n => n.CreatedAt) // 稳定输出：杜绝同级 SortOrder 相同时顺序不稳定
            .Select(n => new NodeDto
            {
                Id = n.Id,
                MindMapId = n.MindMapId,
                ParentId = n.ParentId,
                Title = n.Title,
                Content = n.Content,
                Note = n.Note,
                SortOrder = n.SortOrder,
                IsCollapsed = n.IsCollapsed,
                X = n.X,
                Y = n.Y,
                Width = n.Width,
                Height = n.Height,
                Color = n.Color,
                FontSize = n.FontSize,
                FontFamily = n.FontFamily,
                Shape = (int?)n.Shape,
                Icon = n.Icon,
                BorderColor = n.BorderColor,
                BackgroundColor = n.BackgroundColor,
                EdgeColor = n.EdgeColor,
                EdgeStyle = n.EdgeStyle,
                Direction = n.Direction,
                ExtraData = n.ExtraData,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt
            })
            .ToListAsync(ct);

        return list;
    }

    public async Task<List<NodeTreeNodeDto>> GetTreeAsync(Guid? userId, Guid mindMapId, CancellationToken ct = default)
    {
        var flat = await GetByMindMapAsync(userId, mindMapId, ct);
        return BuildTree(flat);
    }

    public async Task<NodeDto?> GetAsync(Guid? userId, Guid id, CancellationToken ct = default)
    {
        var node = await _db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (node is null) return null;

        await EnsureCanReadAsync(userId, node.MindMapId, ct);
        return ToDto(node);
    }

    public async Task<NodeDto> CreateAsync(Guid userId, Guid mindMapId, NodeCreateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await EnsureCanEditAsync(userId, mindMapId, ct);

        // 验证父节点
        if (req.ParentId.HasValue)
        {
            var parent = await _db.Nodes.FirstOrDefaultAsync(n => n.Id == req.ParentId.Value, ct);
            if (parent is null) throw ApiException.NotFound("ParentNode", req.ParentId.Value);
            if (parent.MindMapId != mindMapId)
                throw ApiException.Conflict("父节点不属于同一导图");
        }

        // 排序：如未指定则追加到同级末尾
        var sortOrder = req.SortOrder ?? await GetNextSortOrderAsync(mindMapId, req.ParentId, ct);

        var node = new Node
        {
            Id = Guid.NewGuid(),
            MindMapId = mindMapId,
            ParentId = req.ParentId,
            Title = req.Title,
            Content = req.Content,
            Note = req.Note,
            SortOrder = sortOrder,
            IsCollapsed = req.IsCollapsed,
            X = req.X,
            Y = req.Y,
            Width = req.Width,
            Height = req.Height,
            Color = req.Color,
            FontSize = req.FontSize,
            FontFamily = req.FontFamily,
            Shape = req.Shape,
            Icon = req.Icon,
            BorderColor = req.BorderColor,
            BackgroundColor = req.BackgroundColor,
            EdgeColor = req.EdgeColor,
            EdgeStyle = req.EdgeStyle,
            Direction = req.Direction,
            ExtraData = req.ExtraData,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Nodes.Add(node);

        // 如导图尚无根节点且新节点为根级，自动设为根
        if (req.ParentId is null)
        {
            var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
            if (map is not null && map.RootNodeId is null)
            {
                map.RootNodeId = node.Id;
            }
        }

        await UpdateMindMapStatsAsync(mindMapId, nodeCountDelta: 1, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(node);
    }

    public async Task<NodeDto> UpdateAsync(Guid userId, Guid id, NodeUpdateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var node = await GetOwnedNodeAsync(userId, id, ct);

        if (req.Title is not null) node.Title = req.Title;
        if (req.Content is not null) node.Content = req.Content;
        if (req.Note is not null) node.Note = req.Note;
        if (req.SortOrder.HasValue) node.SortOrder = req.SortOrder.Value;
        if (req.IsCollapsed.HasValue) node.IsCollapsed = req.IsCollapsed.Value;
        if (req.X.HasValue) node.X = req.X;
        if (req.Y.HasValue) node.Y = req.Y;
        if (req.Width.HasValue) node.Width = req.Width;
        if (req.Height.HasValue) node.Height = req.Height;
        if (req.Color is not null) node.Color = req.Color;
        if (req.FontSize.HasValue) node.FontSize = req.FontSize;
        if (req.FontFamily is not null) node.FontFamily = req.FontFamily;
        if (req.Shape.HasValue) node.Shape = req.Shape;
        if (req.Icon is not null) node.Icon = req.Icon;
        if (req.BorderColor is not null) node.BorderColor = req.BorderColor;
        if (req.BackgroundColor is not null) node.BackgroundColor = req.BackgroundColor;
        if (req.EdgeColor is not null) node.EdgeColor = req.EdgeColor;
        if (req.EdgeStyle.HasValue) node.EdgeStyle = req.EdgeStyle;
        if (req.Direction.HasValue) node.Direction = req.Direction;
        if (req.ExtraData is not null) node.ExtraData = req.ExtraData;

        node.UpdatedAt = DateTime.UtcNow;

        await UpdateMindMapStatsAsync(node.MindMapId, nodeCountDelta: 0, ct);
        await _db.SaveChangesAsync(ct);

        return ToDto(node);
    }

    public async Task<NodeDto> MoveAsync(Guid userId, Guid id, NodeMoveRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        var node = await GetOwnedNodeAsync(userId, id, ct);
        var oldParentId = node.ParentId;

        // 验证新父节点
        if (req.ParentId.HasValue)
        {
            if (req.ParentId.Value == id)
                throw ApiException.Conflict("不能将节点作为自身的子节点");

            // 防止环：新父节点不能是当前节点的子孙
            if (await IsDescendantAsync(req.ParentId.Value, id, ct))
                throw ApiException.Conflict("不能将节点移动到自身子孙下");

            var newParent = await _db.Nodes.FirstOrDefaultAsync(n => n.Id == req.ParentId.Value, ct);
            if (newParent is null) throw ApiException.NotFound("ParentNode", req.ParentId.Value);
            if (newParent.MindMapId != node.MindMapId)
                throw ApiException.Conflict("父节点不属于同一导图");
        }

        var newParentId = req.ParentId;
        var now = DateTime.UtcNow;

        // 阶段1：把受影响父节点（旧/新）下的子节点整体平移到临时排序区间。
        // nodes 表存在 (MindMapId, ParentId, SortOrder) 唯一索引，EF 逐行 UPDATE，
        // 直接写入最终值会与兄弟节点的现有值冲突，必须分两阶段落库。
        foreach (var pid in new[] { oldParentId, newParentId }.Distinct())
        {
            var siblings = await _db.Nodes
                .Where(n => n.MindMapId == node.MindMapId && n.ParentId == pid && n.Id != id)
                .OrderBy(n => n.SortOrder)
                .ToListAsync(ct);
            var temp = 1_000_000;
            foreach (var s in siblings)
            {
                s.SortOrder = temp++;
                s.UpdatedAt = now;
            }
        }
        // 被移动节点也先挪到临时区间（此时仍挂在旧父节点下）
        node.SortOrder = 2_000_000;
        node.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // 阶段2：写入最终值
        node.ParentId = newParentId;
        // 根节点直接子节点的方向随移动一起持久化，避免前端再发一次单独的方向更新
        if (req.Direction.HasValue)
        {
            node.Direction = req.Direction.Value;
        }

        // 目标父节点其余子节点按原有相对顺序压缩为 0..n-1，并把节点插入到请求位置
        var targetSiblings = await _db.Nodes
            .Where(n => n.MindMapId == node.MindMapId && n.ParentId == newParentId && n.Id != id)
            .OrderBy(n => n.SortOrder)
            .ToListAsync(ct);
        var index = Math.Clamp(req.SortOrder ?? targetSiblings.Count, 0, targetSiblings.Count);
        node.SortOrder = index;
        node.UpdatedAt = now;
        var order = 0;
        foreach (var s in targetSiblings)
        {
            if (order == index) order++;
            s.SortOrder = order++;
            s.UpdatedAt = now;
        }

        // 旧父节点剩余子节点压缩为 0..n-1（新旧父相同时上面已处理）
        if (oldParentId != newParentId)
        {
            var oldSiblings = await _db.Nodes
                .Where(n => n.MindMapId == node.MindMapId && n.ParentId == oldParentId && n.Id != id)
                .OrderBy(n => n.SortOrder)
                .ToListAsync(ct);
            var i = 0;
            foreach (var s in oldSiblings)
            {
                s.SortOrder = i++;
                s.UpdatedAt = now;
            }
        }

        await UpdateMindMapStatsAsync(node.MindMapId, nodeCountDelta: 0, ct);
        await _db.SaveChangesAsync(ct);

        return ToDto(node);
    }

    public async Task BatchUpdateAsync(Guid userId, Guid mindMapId, NodeBatchUpdateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        await EnsureCanEditAsync(userId, mindMapId, ct);

        if (req.Nodes.Count == 0) return;

        var ids = req.Nodes.Select(x => x.Id).Distinct().ToList();
        var nodes = await _db.Nodes
            .Where(n => n.MindMapId == mindMapId && ids.Contains(n.Id))
            .ToListAsync(ct);

        var nodeMap = nodes.ToDictionary(n => n.Id);
        var now = DateTime.UtcNow;

        // 阶段1：带 SortOrder 的项先平移到临时排序区间，
        // 避免 (MindMapId, ParentId, SortOrder) 唯一索引在同级排序交换时瞬时冲突。
        var reorderItems = req.Nodes
            .Where(x => x.SortOrder.HasValue && nodeMap.ContainsKey(x.Id))
            .ToList();
        if (reorderItems.Count > 0)
        {
            var temp = 1_000_000;
            foreach (var item in reorderItems)
            {
                var node = nodeMap[item.Id];
                node.SortOrder = temp++;
                node.UpdatedAt = now;
            }
            await _db.SaveChangesAsync(ct);
        }

        // 阶段2：写入最终值
        foreach (var item in req.Nodes)
        {
            if (!nodeMap.TryGetValue(item.Id, out var node)) continue;

            if (item.SortOrder.HasValue) node.SortOrder = item.SortOrder.Value;
            if (item.ParentId.HasValue) node.ParentId = item.ParentId;
            if (item.X.HasValue) node.X = item.X;
            if (item.Y.HasValue) node.Y = item.Y;
            if (item.IsCollapsed.HasValue) node.IsCollapsed = item.IsCollapsed.Value;
            node.UpdatedAt = now;
        }

        await UpdateMindMapStatsAsync(mindMapId, nodeCountDelta: 0, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var node = await GetOwnedNodeAsync(userId, id, ct);
        var mindMapId = node.MindMapId;

        // 递归收集所有子孙节点 Id
        var toDelete = new HashSet<Guid> { id };
        await CollectDescendantsAsync(id, toDelete, ct);

        // 批量删除
        var nodesToRemove = await _db.Nodes
            .Where(n => toDelete.Contains(n.Id))
            .ToListAsync(ct);
        _db.Nodes.RemoveRange(nodesToRemove);

        // 如为根节点，清除 MindMap.RootNodeId
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is not null && map.RootNodeId == id)
        {
            map.RootNodeId = null;
        }

        await UpdateMindMapStatsAsync(mindMapId, nodeCountDelta: -toDelete.Count, ct);
        await _db.SaveChangesAsync(ct);
    }

    // ---- 私有工具方法 ----

    private async Task EnsureCanReadAsync(Guid? userId, Guid mindMapId, CancellationToken ct)
    {
        var map = await _db.MindMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mindMapId, ct)
            ?? throw ApiException.NotFound("MindMap", mindMapId);

        if (!map.IsPublic && (userId is null || map.OwnerId != userId.Value))
            throw ApiException.Forbidden("无访问权限");
    }

    private async Task EnsureCanEditAsync(Guid userId, Guid mindMapId, CancellationToken ct)
    {
        var owned = await _db.MindMaps.AnyAsync(m => m.Id == mindMapId && m.OwnerId == userId, ct);
        if (!owned) throw ApiException.NotFound("MindMap", mindMapId);
    }

    private async Task<Node> GetOwnedNodeAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var node = await _db.Nodes.FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw ApiException.NotFound("Node", id);

        var owned = await _db.MindMaps.AnyAsync(m => m.Id == node.MindMapId && m.OwnerId == userId, ct);
        if (!owned) throw ApiException.Forbidden("无访问权限");

        return node;
    }

    private async Task<int> GetNextSortOrderAsync(Guid mindMapId, Guid? parentId, CancellationToken ct)
    {
        var max = await _db.Nodes
            .Where(n => n.MindMapId == mindMapId && n.ParentId == parentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
    }

    private async Task<bool> IsDescendantAsync(Guid candidateId, Guid ancestorId, CancellationToken ct)
    {
        // BFS 向上查找祖先链，看 candidate 是否最终回到 ancestor
        var current = candidateId;
        var visited = new HashSet<Guid>();
        while (current != Guid.Empty && visited.Add(current))
        {
            var parent = await _db.Nodes.AsNoTracking()
                .Where(n => n.Id == current)
                .Select(n => n.ParentId)
                .FirstOrDefaultAsync(ct);

            if (parent is null) return false;
            if (parent.Value == ancestorId) return true;
            current = parent.Value;
        }
        return false;
    }

    private async Task CollectDescendantsAsync(Guid parentId, HashSet<Guid> acc, CancellationToken ct)
    {
        var childIds = await _db.Nodes.AsNoTracking()
            .Where(n => n.ParentId == parentId)
            .Select(n => n.Id)
            .ToListAsync(ct);

        foreach (var cid in childIds)
        {
            if (acc.Add(cid))
            {
                await CollectDescendantsAsync(cid, acc, ct);
            }
        }
    }

    private async Task UpdateMindMapStatsAsync(Guid mindMapId, int nodeCountDelta, CancellationToken ct)
    {
        var map = await _db.MindMaps.FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is not null)
        {
            if (nodeCountDelta != 0)
            {
                // 增量更新，确保不为负；若当前为 0（如历史遗留数据），则从数据库重新统计
                if (map.NodeCount == 0)
                {
                    map.NodeCount = await _db.Nodes
                        .Where(n => n.MindMapId == mindMapId)
                        .CountAsync(ct);
                }
                else
                {
                    map.NodeCount = Math.Max(0, map.NodeCount + nodeCountDelta);
                }
            }
            map.UpdatedAt = DateTime.UtcNow;
            map.LastEditedAt = DateTime.UtcNow;
        }
    }

    private static NodeDto ToDto(Node n) => new()
    {
        Id = n.Id,
        MindMapId = n.MindMapId,
        ParentId = n.ParentId,
        Title = n.Title,
        Content = n.Content,
        Note = n.Note,
        SortOrder = n.SortOrder,
        IsCollapsed = n.IsCollapsed,
        X = n.X,
        Y = n.Y,
        Width = n.Width,
        Height = n.Height,
        Color = n.Color,
        FontSize = n.FontSize,
        FontFamily = n.FontFamily,
        Shape = (int?)n.Shape,
        Icon = n.Icon,
        BorderColor = n.BorderColor,
        BackgroundColor = n.BackgroundColor,
        EdgeColor = n.EdgeColor,
        EdgeStyle = n.EdgeStyle,
        Direction = n.Direction,
        ExtraData = n.ExtraData,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };

    private static List<NodeTreeNodeDto> BuildTree(List<NodeDto> flat)
    {
        var lookup = flat.ToLookup(n => n.ParentId);
        var roots = lookup[null].OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedAt).ToList();

        List<NodeTreeNodeDto> BuildChildren(NodeDto parent)
        {
            return lookup[parent.Id]
                .OrderBy(n => n.SortOrder)
                .ThenBy(n => n.CreatedAt)
                .Select(child => ToTreeNodeDto(child, BuildChildren(child)))
                .ToList();
        }

        return roots.Select(r => ToTreeNodeDto(r, BuildChildren(r))).ToList();
    }

    /// <summary>
    /// 将扁平节点 DTO 转为树节点 DTO，统一字段赋值（含 Direction），避免多处手写遗漏字段。
    /// </summary>
    private static NodeTreeNodeDto ToTreeNodeDto(NodeDto n, List<NodeTreeNodeDto> children) => new()
    {
        Id = n.Id,
        MindMapId = n.MindMapId,
        ParentId = n.ParentId,
        Title = n.Title,
        Content = n.Content,
        Note = n.Note,
        SortOrder = n.SortOrder,
        IsCollapsed = n.IsCollapsed,
        X = n.X,
        Y = n.Y,
        Width = n.Width,
        Height = n.Height,
        Color = n.Color,
        FontSize = n.FontSize,
        FontFamily = n.FontFamily,
        Shape = n.Shape,
        Icon = n.Icon,
        BorderColor = n.BorderColor,
        BackgroundColor = n.BackgroundColor,
        EdgeColor = n.EdgeColor,
        EdgeStyle = n.EdgeStyle,
        Direction = n.Direction,
        ExtraData = n.ExtraData,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        Children = children
    };
}
