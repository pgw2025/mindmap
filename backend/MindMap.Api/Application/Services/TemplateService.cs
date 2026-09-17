using System.Text.Json;
using Microsoft.AspNetCore.Http;
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

    // 导入导出
    Task<TemplateExportFile> ExportAsync(Guid id, CancellationToken ct = default);
    Task<TemplateExportBatchFile> ExportAllAsync(CancellationToken ct = default);
    Task<TemplateImportResult> ImportAsync(IFormFile file, CancellationToken ct = default);
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

    // ===================== 导入导出 =====================

    private const long MaxImportFileSizeBytes = 5 * 1024 * 1024; // 5MB
    private const string ExportFormat = "mindmap-template";
    private const int ExportVersion = 1;

    public async Task<TemplateExportFile> ExportAsync(Guid id, CancellationToken ct = default)
    {
        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ApiException.NotFound("Template", id);

        return new TemplateExportFile
        {
            Format = ExportFormat,
            Version = ExportVersion,
            ExportedAt = DateTime.UtcNow,
            Template = BuildExportPayload(template)
        };
    }

    public async Task<TemplateExportBatchFile> ExportAllAsync(CancellationToken ct = default)
    {
        var templates = await _db.Templates
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return new TemplateExportBatchFile
        {
            Format = ExportFormat,
            Version = ExportVersion,
            ExportedAt = DateTime.UtcNow,
            Templates = templates.Select(BuildExportPayload).ToList()
        };
    }

    public async Task<TemplateImportResult> ImportAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("请选择文件");
        if (file.Length > MaxImportFileSizeBytes)
            throw ApiException.BadRequest($"文件过大，上限 {MaxImportFileSizeBytes / 1024 / 1024}MB");

        // 读取并解析顶层 JSON
        JsonDocument doc;
        try
        {
            await using var stream = file.OpenReadStream();
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw ApiException.BadRequest($"JSON 解析失败：{ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw ApiException.BadRequest("文件结构不合法：顶层必须是对象");

            // format 校验
            if (!root.TryGetProperty("format", out var fmt) ||
                fmt.ValueKind != JsonValueKind.String ||
                fmt.GetString() != ExportFormat)
                throw ApiException.BadRequest("不是有效的模板导出文件（format 标识缺失或不匹配）");

            // version 校验
            if (!root.TryGetProperty("version", out var ver) || !ver.TryGetInt32(out var version))
                throw ApiException.BadRequest("文件缺少版本号");
            if (version != ExportVersion)
                throw ApiException.BadRequest($"不支持的版本 {version}，当前仅支持 {ExportVersion}");

            // 提取 payload 列表（单条或批量）
            var payloads = ExtractPayloads(root);
            if (payloads.Count == 0)
                throw ApiException.BadRequest("文件中没有可导入的模板");

            // 已存在的 Name 集合，用于重名跳过
            var existingNames = await _db.Templates.Select(t => t.Name).ToListAsync(ct);
            var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

            // 导入时统一追加排序值：从当前最大 SortOrder 之后开始
            var maxSortOrder = await _db.Templates.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;
            var nextSort = maxSortOrder + 1;

            var result = new TemplateImportResult();
            var now = DateTime.UtcNow;

            foreach (var payload in payloads)
            {
                var itemName = NormalizeName(payload.Name);
                try
                {
                    // 基础校验
                    if (string.IsNullOrWhiteSpace(itemName))
                    {
                        result.Failed.Add(new TemplateImportFailedItem { Name = itemName, Reason = "名称为空" });
                        continue;
                    }
                    if (itemName.Length > 64)
                    {
                        result.Failed.Add(new TemplateImportFailedItem { Name = itemName, Reason = "名称超过 64 字符" });
                        continue;
                    }

                    // ConfigJson 必须为对象
                    if (payload.ConfigJson.ValueKind != JsonValueKind.Object)
                    {
                        result.Failed.Add(new TemplateImportFailedItem { Name = itemName, Reason = "configJson 必须是 JSON 对象" });
                        continue;
                    }

                    // 重名跳过
                    if (existingSet.Contains(itemName))
                    {
                        result.Skipped++;
                        continue;
                    }

                    var template = new Template
                    {
                        Id = Guid.NewGuid(),
                        Name = itemName,
                        Description = TrimToMax(payload.Description, 512),
                        SortOrder = nextSort++,
                        IsEnabled = payload.IsEnabled,
                        ConfigJson = payload.ConfigJson.GetRawText(),
                        InitialStructureJson = payload.InitialStructureJson is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined }
                            ? payload.InitialStructureJson.Value.GetRawText()
                            : string.Empty,
                        SwatchJson = payload.SwatchJson is { ValueKind: JsonValueKind.Object }
                            ? payload.SwatchJson.Value.GetRawText()
                            : BuildSwatchJson(payload.ConfigJson),
                        CreatedById = null,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    _db.Templates.Add(template);
                    existingSet.Add(itemName);
                    result.Created++;
                }
                catch (Exception ex)
                {
                    result.Failed.Add(new TemplateImportFailedItem { Name = itemName, Reason = ex.Message });
                }
            }

            await _db.SaveChangesAsync(ct);
            return result;
        }
    }

    /// <summary>从顶层 JSON 提取单条/批量 payload 列表。</summary>
    private static List<TemplateExportPayload> ExtractPayloads(JsonElement root)
    {
        var payloads = new List<TemplateExportPayload>();

        // 批量：templates 数组
        if (root.TryGetProperty("templates", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    payloads.Add(ParsePayload(item));
            }
            return payloads;
        }

        // 单条：template 对象
        if (root.TryGetProperty("template", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            payloads.Add(ParsePayload(single));
        }

        return payloads;
    }

    private static TemplateExportPayload ParsePayload(JsonElement el)
    {
        var payload = new TemplateExportPayload();

        if (el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            payload.Name = nameEl.GetString() ?? string.Empty;
        if (el.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
            payload.Description = descEl.GetString();
        if (el.TryGetProperty("sortOrder", out var soEl) && soEl.TryGetInt32(out var so))
            payload.SortOrder = so;
        if (el.TryGetProperty("isEnabled", out var ieEl))
            payload.IsEnabled = ieEl.ValueKind != JsonValueKind.False;
        if (el.TryGetProperty("configJson", out var cfgEl))
            payload.ConfigJson = cfgEl.Clone();
        if (el.TryGetProperty("initialStructureJson", out var initEl))
            payload.InitialStructureJson = initEl.Clone();
        if (el.TryGetProperty("swatchJson", out var swEl))
            payload.SwatchJson = swEl.Clone();

        return payload;
    }

    private static TemplateExportPayload BuildExportPayload(Template t)
    {
        return new TemplateExportPayload
        {
            Name = t.Name,
            Description = t.Description,
            SortOrder = t.SortOrder,
            IsEnabled = t.IsEnabled,
            ConfigJson = ParseToElement(t.ConfigJson),
            InitialStructureJson = string.IsNullOrWhiteSpace(t.InitialStructureJson)
                ? null
                : ParseToElement(t.InitialStructureJson),
            SwatchJson = string.IsNullOrWhiteSpace(t.SwatchJson) ? null : ParseToElement(t.SwatchJson)
        };
    }

    /// <summary>将存储的 JSON 字符串解析为 JsonElement（保证导出为结构化对象）。</summary>
    private static JsonElement ParseToElement(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.Clone();
        }
        catch
        {
            // 兜底：非标准 JSON 时包裹为字符串，避免导出失败
            using var d = JsonDocument.Parse(JsonSerializer.Serialize(json));
            return d.RootElement.Clone();
        }
    }

    private static string? BuildSwatchJson(JsonElement config)
    {
        // 尝试从 configJson 提取色板；失败则返回 null
        try
        {
            if (config.ValueKind != JsonValueKind.Object) return null;
            string? Get(string key)
            {
                if (config.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
                return null;
            }

            var rootFill = Get("root") is null ? null : ExtractFill(config, "root");
            if (rootFill is null) return null;

            var swatch = new
            {
                rootFill,
                secondFill = ExtractFill(config, "second"),
                lineColor = Get("lineColor"),
                bg = Get("backgroundColor")
            };
            return JsonSerializer.Serialize(swatch);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractFill(JsonElement config, string levelKey)
    {
        if (config.ValueKind != JsonValueKind.Object) return null;
        if (!config.TryGetProperty(levelKey, out var level) || level.ValueKind != JsonValueKind.Object) return null;
        if (level.TryGetProperty("fillColor", out var fill) && fill.ValueKind == JsonValueKind.String)
            return fill.GetString();
        return null;
    }

    private static string NormalizeName(string? name) => (name ?? string.Empty).Trim();

    private static string? TrimToMax(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length > max ? s[..max] : s;
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
