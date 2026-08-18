using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class MindMapReportConfiguration : IEntityTypeConfiguration<MindMapReport>
{
    public void Configure(EntityTypeBuilder<MindMapReport> builder)
    {
        builder.ToTable("mindmap_reports");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnType("char(36)");
        builder.Property(r => r.MindMapId).HasColumnType("char(36)");
        builder.Property(r => r.ReporterId).HasColumnType("char(36)");
        builder.Property(r => r.ResolvedById).HasColumnType("char(36)");

        builder.Property(r => r.Reason).HasMaxLength(512).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ResolutionNote).HasMaxLength(512);
        builder.Property(r => r.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(r => r.ResolvedAt).HasColumnType("datetime(3)");

        // 按导图 + 状态倒序（后台待审核列表）
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.MindMapId);

        builder.HasOne(r => r.MindMap)
            .WithMany(m => m.Reports)
            .HasForeignKey(r => r.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.ResolvedBy)
            .WithMany()
            .HasForeignKey(r => r.ResolvedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
