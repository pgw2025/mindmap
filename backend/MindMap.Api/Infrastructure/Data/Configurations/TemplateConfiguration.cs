using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnType("char(36)");
        // CreatedById 可空：FK 为 ON DELETE SET NULL，必须允许 NULL
        builder.Property(t => t.CreatedById).HasColumnType("char(36)").IsRequired(false);

        builder.Property(t => t.Name).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(512);
        // 样式/结构 JSON 可能较大，使用 longtext
        builder.Property(t => t.ConfigJson).HasColumnType("longtext").IsRequired();
        builder.Property(t => t.InitialStructureJson).HasColumnType("longtext");
        builder.Property(t => t.SwatchJson).HasMaxLength(512);

        builder.Property(t => t.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(t => t.UpdatedAt).HasColumnType("datetime(3)");

        // 列表默认按 SortOrder + 创建时间排序
        builder.HasIndex(t => new { t.SortOrder, t.CreatedAt });
        // 启用筛选索引：普通用户只看启用的
        builder.HasIndex(t => new { t.IsEnabled, t.SortOrder });

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
