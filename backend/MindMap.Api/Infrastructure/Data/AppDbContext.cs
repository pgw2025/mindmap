using Microsoft.EntityFrameworkCore;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data;

/// <summary>
/// 应用主 DbContext。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MindMapEntity> MindMaps => Set<MindMapEntity>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<MindMapVersion> MindMapVersions => Set<MindMapVersion>();
    public DbSet<MindMapShare> MindMapShares => Set<MindMapShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 通过程序集中所有 IEntityTypeConfiguration 自动注册实体配置
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
