using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Templates;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Common.Responses;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface ITemplateService
{
    // 公共（普通用户）：仅返回启用的模板
    Task<List<TemplateListItemDto>> GetEnabledListAsync(CancellationToken ct = default);
    Task<TemplateDetailDto?> GetEnabledAsync(Guid id, CancellationToken ct = default);

    // 管理端
    Task<PagedResult<AdminTemplateListItemDto>> GetAdminListAsync(AdminTemplateListQuery query, CancellationToken ct = default);
    Task<TemplateDetailDto> GetAdminAsync(Guid id, CancellationToken ct = default);
    Task<TemplateDetailDto> CreateAsync(Guid operatorUserId, TemplateCreateRequest req, CancellationToken ct = default);
    Task<TemplateDetailDto> UpdateAsync(Guid id, TemplateUpdateRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _db;

    public TemplateService(AppDbContext db)
    {
        _db = db;
    }

    // ===================== 公共接口 =====================

    public async Task<List<TemplateListItemDto>> GetEnabledListAsync(CancellationToken ct = default)
    {
        return await _db.Templates
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new TemplateListItemDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                SortOrder = t.SortOrder,
                SwatchJson = t.SwatchJson,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<TemplateDetailDto?> GetEnabledAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Templates
            .Where(t => t.Id == id && t.IsEnabled)
            .Select(t => new TemplateDetailDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                SortOrder = t.SortOrder,
                IsEnabled = t.IsEnabled,
                ConfigJson = t.ConfigJson,
                InitialStructureJson = t.InitialStructureJson,
                SwatchJson = t.SwatchJson,
                CreatedById = t.CreatedById,
                CreatedByName = t.CreatedBy != null ? t.CreatedBy.Username : null,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);
    }

    // ===================== 管理端接口 =====================

    public async Task<PagedResult<AdminTemplateListItemDto>> GetAdminListAsync(AdminTemplateListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);
        var scope = string.IsNullOrWhiteSpace(query.Scope) ? "all" : query.Scope.ToLowerInvariant();

        IQueryable<Template> q = _db.Templates;

        if (scope == "enabled") q = q.Where(t => t.IsEnabled);
        else if (scope == "disabled") q = q.Where(t => !t.IsEnabled);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(t => t.Name.Contains(kw) || (t.Description != null && t.Description.Contains(kw)));
        }

        var total = await q.LongCountAsync(ct);

        var items = await q
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new AdminTemplateListItemDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                SortOrder = t.SortOrder,
                IsEnabled = t.IsEnabled,
                SwatchJson = t.SwatchJson,
                CreatedById = t.CreatedById,
                CreatedByName = t.CreatedBy != null ? t.CreatedBy.Username : null,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(ct);

        return PagedResult<AdminTemplateListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task<TemplateDetailDto> GetAdminAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Templates
            .Where(t => t.Id == id)
            .Select(t => new TemplateDetailDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                SortOrder = t.SortOrder,
                IsEnabled = t.IsEnabled,
                ConfigJson = t.ConfigJson,
                InitialStructureJson = t.InitialStructureJson,
                SwatchJson = t.SwatchJson,
                CreatedById = t.CreatedById,
                CreatedByName = t.CreatedBy != null ? t.CreatedBy.Username : null,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Template", id);
    }

    public async Task<TemplateDetailDto> CreateAsync(Guid operatorUserId, TemplateCreateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        ValidateJson(req.ConfigJson, nameof(req.ConfigJson));
        ValidateJson(req.InitialStructureJson, nameof(req.InitialStructureJson));

        var template = new Template
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            SortOrder = req.SortOrder,
            IsEnabled = req.IsEnabled,
            ConfigJson = req.ConfigJson,
            InitialStructureJson = req.InitialStructureJson ?? string.Empty,
            SwatchJson = req.SwatchJson,
            CreatedById = operatorUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Templates.Add(template);
        await _db.SaveChangesAsync(ct);
        return await GetAdminAsync(template.Id, ct);
    }

    public async Task<TemplateDetailDto> UpdateAsync(Guid id, TemplateUpdateRequest req, CancellationToken ct = default)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("Template", id);

        if (req.Name is not null) template.Name = req.Name;
        if (req.Description is not null) template.Description = req.Description;
        if (req.SortOrder.HasValue) template.SortOrder = req.SortOrder.Value;
        if (req.IsEnabled.HasValue) template.IsEnabled = req.IsEnabled.Value;
        if (req.ConfigJson is not null)
        {
            ValidateJson(req.ConfigJson, nameof(req.ConfigJson));
            template.ConfigJson = req.ConfigJson;
        }
        if (req.InitialStructureJson is not null)
        {
            ValidateJson(req.InitialStructureJson, nameof(req.InitialStructureJson));
            template.InitialStructureJson = req.InitialStructureJson;
        }
        if (req.SwatchJson is not null) template.SwatchJson = req.SwatchJson;

        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetAdminAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("Template", id);
        _db.Templates.Remove(template);
        await _db.SaveChangesAsync(ct);
    }

    private static void ValidateJson(string json, string paramName)
    {
        // 允许空字符串（初始结构可为空）
        if (string.IsNullOrWhiteSpace(json)) return;
        var trimmed = json.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            throw new ApiException($"{paramName} 必须是合法的 JSON 对象或数组");
    }
}
