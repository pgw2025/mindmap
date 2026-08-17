using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class MindMapVersionConfiguration : IEntityTypeConfiguration<MindMapVersion>
{
    public void Configure(EntityTypeBuilder<MindMapVersion> builder)
    {
        builder.ToTable("mindmap_versions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id).HasColumnType("char(36)");
        builder.Property(v => v.MindMapId).HasColumnType("char(36)");

        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.Remark).HasMaxLength(256);
        builder.Property(v => v.NodeSnapshotJson).HasColumnType("longtext");
        builder.Property(v => v.NodeCount).IsRequired();

        builder.Property(v => v.CreatedById).HasColumnType("char(36)");
        builder.Property(v => v.CreatedAt).HasColumnType("datetime(3)");

        // 唯一约束：同一导图版本号不重复，同时支持按导图倒序查询版本
        builder.HasIndex(v => new { v.MindMapId, v.VersionNumber })
            .IsUnique()
            .IsDescending(false, true)
            .HasDatabaseName("IX_mindmap_versions_mindmap_version");

        builder.HasOne(v => v.MindMap)
            .WithMany()
            .HasForeignKey(v => v.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.CreatedBy)
            .WithMany()
            .HasForeignKey(v => v.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
