using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasColumnType("char(36)");
        builder.Property(f => f.UserId).HasColumnType("char(36)");
        builder.Property(f => f.ParentId).HasColumnType("char(36)");

        builder.Property(f => f.Name).HasMaxLength(64).IsRequired();
        builder.Property(f => f.SortOrder);
        builder.Property(f => f.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(f => f.UpdatedAt).HasColumnType("datetime(3)");

        // 用户文件夹树查询：按 (UserId, ParentId, SortOrder) 复合索引
        builder.HasIndex(f => new { f.UserId, f.ParentId, f.SortOrder });

        builder.HasOne(f => f.User)
            .WithMany(u => u.Folders)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // 自引用父级关系：MySQL 不允许循环 CASCADE，必须 Restrict
        builder.HasOne(f => f.Parent)
            .WithMany(p => p.Children!)
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
