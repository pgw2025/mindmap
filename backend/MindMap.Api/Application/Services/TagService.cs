using Microsoft.EntityFrameworkCore;
using MindMap.Api.Application.DTOs.Tags;
using MindMap.Api.Common.Exceptions;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Infrastructure.Data;

namespace MindMap.Api.Application.Services;

public interface ITagService
{
    Task<IReadOnlyList<TagDto>> GetAllAsync(Guid userId, CancellationToken ct = default);
    Task<TagDto> CreateAsync(Guid userId, TagCreateRequest req, CancellationToken ct = default);
    Task<TagDto> UpdateAsync(Guid userId, Guid id, TagUpdateRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class TagService : ITagService
{
    private readonly AppDbContext _db;

    public TagService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TagDto>> GetAllAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.Tags
            .Where(t => t.UserId == userId)
            .Select(t => new TagDto
            {
                Id = t.Id,
                Name = t.Name,
                Color = t.Color,
                CreatedAt = t.CreatedAt,
                MindMapCount = t.MindMaps.Count
            })
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return list;
    }

    public async Task<TagDto> CreateAsync(Guid userId, TagCreateRequest req, CancellationToken ct = default)
    {
        var exists = await _db.Tags.AnyAsync(t => t.UserId == userId && t.Name == req.Name, ct);
        if (exists) throw ApiException.Conflict("同名标签已存在");

        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = req.Name,
            Color = string.IsNullOrWhiteSpace(req.Color) ? "#18a058" : req.Color,
            CreatedAt = DateTime.UtcNow
        };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(ct);
        return new TagDto { Id = tag.Id, Name = tag.Name, Color = tag.Color, CreatedAt = tag.CreatedAt, MindMapCount = 0 };
    }

    public async Task<TagDto> UpdateAsync(Guid userId, Guid id, TagUpdateRequest req, CancellationToken ct = default)
    {
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
            ?? throw ApiException.NotFound("Tag", id);

        if (!string.IsNullOrWhiteSpace(req.Name))
        {
            var dup = await _db.Tags.AnyAsync(t => t.Id != id && t.UserId == userId && t.Name == req.Name, ct);
            if (dup) throw ApiException.Conflict("同名标签已存在");
            tag.Name = req.Name;
        }
        if (!string.IsNullOrWhiteSpace(req.Color)) tag.Color = req.Color;

        await _db.SaveChangesAsync(ct);
        return new TagDto { Id = tag.Id, Name = tag.Name, Color = tag.Color, CreatedAt = tag.CreatedAt, MindMapCount = 0 };
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
            ?? throw ApiException.NotFound("Tag", id);
        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync(ct);
    }
}
