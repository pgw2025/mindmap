using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;
using MindMap.Api.Domain.Entities.Enums;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class NodeConfiguration : IEntityTypeConfiguration<Node>
{
    public void Configure(EntityTypeBuilder<Node> builder)
    {
        builder.ToTable("nodes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasColumnType("char(36)");
        builder.Property(n => n.MindMapId).HasColumnType("char(36)");
        builder.Property(n => n.ParentId).HasColumnType("char(36)");

        builder.Property(n => n.Title).HasMaxLength(512).IsRequired();
        builder.Property(n => n.Content).HasMaxLength(16384);
        builder.Property(n => n.Note).HasMaxLength(4096);
        builder.Property(n => n.SortOrder).IsRequired();
        builder.Property(n => n.IsCollapsed).IsRequired();

        builder.Property(n => n.X).HasPrecision(12, 4);
        builder.Property(n => n.Y).HasPrecision(12, 4);
        builder.Property(n => n.Width).HasPrecision(8, 2);
        builder.Property(n => n.Height).HasPrecision(8, 2);

        builder.Property(n => n.Color).HasMaxLength(32);
        builder.Property(n => n.FontSize);
        builder.Property(n => n.FontFamily).HasMaxLength(64);
        builder.Property(n => n.Shape).HasConversion<int>();
        builder.Property(n => n.Icon).HasMaxLength(128);
        builder.Property(n => n.BorderColor).HasMaxLength(32);
        builder.Property(n => n.BackgroundColor).HasMaxLength(32);
        builder.Property(n => n.EdgeColor).HasMaxLength(32);
        builder.Property(n => n.EdgeStyle).HasConversion<int>();
        builder.Property(n => n.ExtraData).HasMaxLength(32768);

        builder.Property(n => n.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(n => n.UpdatedAt).HasColumnType("datetime(3)");

        // 同一导图内按 SortOrder 排序
        builder.HasIndex(n => new { n.MindMapId, n.ParentId, n.SortOrder });
        // 按导图查找所有节点
        builder.HasIndex(n => n.MindMapId);

        builder.HasOne(n => n.MindMap)
            .WithMany(m => m.Nodes)
            .HasForeignKey(n => n.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);

        // 自引用：子节点 -> 父节点
        builder.HasOne(n => n.Parent)
            .WithMany(n => n.Children)
            .HasForeignKey(n => n.ParentId)
            .OnDelete(DeleteBehavior.Restrict); // 删父节点时禁止级联，需手动处理
    }
}
