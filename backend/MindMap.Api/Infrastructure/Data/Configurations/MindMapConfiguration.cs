using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class MindMapConfiguration : IEntityTypeConfiguration<MindMapEntity>
{
    public void Configure(EntityTypeBuilder<MindMapEntity> builder)
    {
        builder.ToTable("mindmaps");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnType("char(36)");
        builder.Property(m => m.OwnerId).HasColumnType("char(36)");
        builder.Property(m => m.FolderId).HasColumnType("char(36)");
        builder.Property(m => m.RootNodeId).HasColumnType("char(36)");

        builder.Property(m => m.Title).HasMaxLength(128).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(2048);
        builder.Property(m => m.CoverImage).HasMaxLength(512);
        builder.Property(m => m.Theme).HasMaxLength(64);
        builder.Property(m => m.DefaultLayout).HasConversion<int>();

        builder.Property(m => m.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(m => m.UpdatedAt).HasColumnType("datetime(3)");
        builder.Property(m => m.LastEditedAt).HasColumnType("datetime(3)");
        builder.Property(m => m.TakenDownAt).HasColumnType("datetime(3)");
        builder.Property(m => m.TakenDownReason).HasMaxLength(256);

        // "我的导图" 列表：按用户倒序
        builder.HasIndex(m => new { m.OwnerId, m.UpdatedAt });
        // "公开导图" 列表：按 IsPublic + LastEditedAt 倒序
        builder.HasIndex(m => new { m.IsPublic, m.LastEditedAt });

        builder.HasOne(m => m.Owner)
            .WithMany(u => u.MindMaps)
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Folder)
            .WithMany(f => f.MindMaps)
            .HasForeignKey(m => m.FolderId)
            .OnDelete(DeleteBehavior.SetNull); // 删文件夹后导图保留为"未分类"

        // 根节点导航（一对一，可选）
        builder.HasOne(m => m.RootNode)
            .WithOne()
            .HasForeignKey<MindMapEntity>(m => m.RootNodeId)
            .OnDelete(DeleteBehavior.Restrict); // 删节点时需先清除 RootNodeId

        // 多对多 MindMap <-> Tag
        builder.HasMany(m => m.Tags)
            .WithMany(t => t.MindMaps)
            .UsingEntity<Dictionary<string, object>>(
                "mindmap_tags",
                j => j.HasOne<Tag>().WithMany().HasForeignKey("TagId").HasPrincipalKey(t => t.Id).OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<MindMapEntity>().WithMany().HasForeignKey("MindMapId").HasPrincipalKey(m => m.Id).OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("MindMapId", "TagId");
                    j.IndexerProperty<Guid>("MindMapId").HasColumnType("char(36)");
                    j.IndexerProperty<Guid>("TagId").HasColumnType("char(36)");
                });
    }
}
