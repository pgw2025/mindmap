using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.MindMaps;
using MindMap.Api.Application.DTOs.Nodes;
using MindMap.Api.Application.DTOs.Shares;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface IShareService
{
    Task<List<ShareListDto>> GetSharesAsync(Guid userId, Guid mindMapId, CancellationToken ct = default);
    Task<ShareDto> CreateShareAsync(Guid userId, Guid mindMapId, ShareCreateRequest req, CancellationToken ct = default);
    Task DeleteShareAsync(Guid userId, Guid shareId, CancellationToken ct = default);
    Task<ShareVerifyResponse> VerifyShareTokenAsync(string token, string? password, CancellationToken ct = default);
    Task<ShareMindMapResponse> GetSharedMindMapAsync(string token, string? password, CancellationToken ct = default);
}

public class ShareService : IShareService
{
    private readonly AppDbContext _db;

    public ShareService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ShareListDto>> GetSharesAsync(Guid userId, Guid mindMapId, CancellationToken ct)
    {
        var map = await _db.MindMaps.AsNoTracking()
            .Select(m => new { m.Id, m.OwnerId })
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId) throw ApiException.Forbidden("无权访问");

        return await _db.MindMapShares.AsNoTracking()
            .Where(s => s.MindMapId == mindMapId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ShareListDto
            {
                Id = s.Id,
                MindMapId = s.MindMapId,
                ShareToken = s.ShareToken,
                HasPassword = s.Password != null,
                ExpiresAt = s.ExpiresAt,
                MaxAccessCount = s.MaxAccessCount,
                AccessCount = s.AccessCount,
                AllowCopy = s.AllowCopy,
                IsDisabled = s.IsDisabled,
                CreatedAt = s.CreatedAt,
                LastAccessedAt = s.LastAccessedAt
            })
            .ToListAsync(ct);
    }

    public async Task<ShareDto> CreateShareAsync(Guid userId, Guid mindMapId, ShareCreateRequest req, CancellationToken ct)
    {
        var map = await _db.MindMaps
            .FirstOrDefaultAsync(m => m.Id == mindMapId, ct);
        if (map is null) throw ApiException.NotFound("导图", mindMapId);
        if (map.OwnerId != userId) throw ApiException.Forbidden("无权操作");

        if (req.SetPublic == true)
        {
            map.IsPublic = true;
            map.UpdatedAt = DateTime.UtcNow;
        }

        var share = new MindMapShare
        {
            Id = Guid.NewGuid(),
            MindMapId = mindMapId,
            ShareToken = GenerateShareToken(),
            Password = string.IsNullOrWhiteSpace(req.Password) ? null : req.Password.Trim(),
            ExpiresAt = req.ExpiresAt,
            MaxAccessCount = req.MaxAccessCount,
            AccessCount = 0,
            AllowCopy = req.AllowCopy,
            IsDisabled = false,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = null
        };

        _db.MindMapShares.Add(share);
        await _db.SaveChangesAsync(ct);

        return new ShareDto
        {
            Id = share.Id,
            MindMapId = share.MindMapId,
            ShareToken = share.ShareToken,
            Password = share.Password,
            ExpiresAt = share.ExpiresAt,
            MaxAccessCount = share.MaxAccessCount,
            AccessCount = share.AccessCount,
            AllowCopy = share.AllowCopy,
            IsDisabled = share.IsDisabled,
            CreatedAt = share.CreatedAt,
            LastAccessedAt = share.LastAccessedAt
        };
    }

    public async Task DeleteShareAsync(Guid userId, Guid shareId, CancellationToken ct)
    {
        var share = await _db.MindMapShares
            .FirstOrDefaultAsync(s => s.Id == shareId, ct);
        if (share is null) throw ApiException.NotFound("分享", shareId);
        if (share.CreatedById != userId) throw ApiException.Forbidden("无权操作");

        _db.MindMapShares.Remove(share);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ShareVerifyResponse> VerifyShareTokenAsync(string token, string? password, CancellationToken ct)
    {
        var share = await _db.MindMapShares
            .Include(s => s.MindMap)
            .ThenInclude(m => m.Owner)
            .FirstOrDefaultAsync(s => s.ShareToken == token, ct);

        if (share is null)
        {
            return new ShareVerifyResponse
            {
                Success = false,
                Message = "分享链接不存在或已被删除"
            };
        }

        if (share.IsDisabled)
        {
            return new ShareVerifyResponse
            {
                Success = false,
                Message = "分享已被禁用"
            };
        }

        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
        {
            return new ShareVerifyResponse
            {
                Success = false,
                Message = "分享链接已过期"
            };
        }

        if (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value)
        {
            return new ShareVerifyResponse
            {
                Success = false,
                Message = "访问次数已达上限"
            };
        }

        if (!string.IsNullOrEmpty(share.Password))
        {
            if (string.IsNullOrEmpty(password) || password != share.Password)
            {
                return new ShareVerifyResponse
                {
                    Success = false,
                    Message = "请输入访问密码",
                    NeedsPassword = true
                };
            }
        }

        share.AccessCount++;
        share.LastAccessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new ShareVerifyResponse
        {
            Success = true,
            MindMapId = share.MindMapId,
            Title = share.MindMap.Title,
            OwnerId = share.MindMap.OwnerId,
            OwnerName = share.MindMap.Owner.Username,
            AllowCopy = share.AllowCopy,
            AccessToken = GenerateAccessToken(share.ShareToken)
        };
    }

    public async Task<ShareMindMapResponse> GetSharedMindMapAsync(string token, string? password, CancellationToken ct)
    {
        var share = await _db.MindMapShares
            .Include(s => s.MindMap)
            .ThenInclude(m => m.Owner)
            .FirstOrDefaultAsync(s => s.ShareToken == token, ct);

        if (share is null)
            throw ApiException.NotFound("分享链接", token);
        if (share.IsDisabled)
            throw ApiException.Forbidden("分享已被禁用");
        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
            throw ApiException.Forbidden("分享链接已过期");
        if (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value)
            throw ApiException.Forbidden("访问次数已达上限");
        if (!string.IsNullOrEmpty(share.Password) && password != share.Password)
            throw ApiException.Forbidden("访问密码错误");

        var mindMapId = share.MindMapId;

        var mindMap = await _db.MindMaps
            .Where(m => m.Id == mindMapId)
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

        if (mindMap is null)
            throw ApiException.NotFound("导图", mindMapId);

        var nodes = await _db.Nodes
            .Where(n => n.MindMapId == mindMapId)
            .OrderBy(n => n.ParentId == null ? 0 : 1)
            .ThenBy(n => n.SortOrder)
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
                EdgeStyle = (int?)n.EdgeStyle,
                ExtraData = n.ExtraData,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt
            })
            .ToListAsync(ct);

        var tree = BuildTree(nodes);

        return new ShareMindMapResponse
        {
            MindMap = mindMap,
            Nodes = tree
        };
    }

    private static string GenerateShareToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var result = new char[16];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }

    private static string GenerateAccessToken(string shareToken)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{shareToken}-{DateTime.UtcNow:yyyyMMddHHmmss}"));
    }

    private static List<NodeTreeNodeDto> BuildTree(List<NodeDto> flat)
    {
        var lookup = flat.ToLookup(n => n.ParentId);
        var roots = lookup[null].OrderBy(n => n.SortOrder).ToList();

        List<NodeTreeNodeDto> BuildChildren(NodeDto parent)
        {
            return lookup[parent.Id]
                .OrderBy(n => n.SortOrder)
                .Select(child => new NodeTreeNodeDto
                {
                    Id = child.Id,
                    MindMapId = child.MindMapId,
                    ParentId = child.ParentId,
                    Title = child.Title,
                    Content = child.Content,
                    Note = child.Note,
                    SortOrder = child.SortOrder,
                    IsCollapsed = child.IsCollapsed,
                    X = child.X,
                    Y = child.Y,
                    Width = child.Width,
                    Height = child.Height,
                    Color = child.Color,
                    FontSize = child.FontSize,
                    FontFamily = child.FontFamily,
                    Shape = child.Shape,
                    Icon = child.Icon,
                    BorderColor = child.BorderColor,
                    BackgroundColor = child.BackgroundColor,
                    EdgeColor = child.EdgeColor,
                    EdgeStyle = child.EdgeStyle,
                    ExtraData = child.ExtraData,
                    CreatedAt = child.CreatedAt,
                    UpdatedAt = child.UpdatedAt,
                    Children = BuildChildren(child)
                })
                .ToList();
        }

        return roots.Select(r => new NodeTreeNodeDto
        {
            Id = r.Id,
            MindMapId = r.MindMapId,
            ParentId = r.ParentId,
            Title = r.Title,
            Content = r.Content,
            Note = r.Note,
            SortOrder = r.SortOrder,
            IsCollapsed = r.IsCollapsed,
            X = r.X,
            Y = r.Y,
            Width = r.Width,
            Height = r.Height,
            Color = r.Color,
            FontSize = r.FontSize,
            FontFamily = r.FontFamily,
            Shape = r.Shape,
            Icon = r.Icon,
            BorderColor = r.BorderColor,
            BackgroundColor = r.BackgroundColor,
            EdgeColor = r.EdgeColor,
            EdgeStyle = r.EdgeStyle,
            ExtraData = r.ExtraData,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            Children = BuildChildren(r)
        }).ToList();
    }
}
