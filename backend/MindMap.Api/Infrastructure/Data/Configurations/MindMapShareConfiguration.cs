using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class MindMapShareConfiguration : IEntityTypeConfiguration<MindMapShare>
{
    public void Configure(EntityTypeBuilder<MindMapShare> builder)
    {
        builder.ToTable("mindmap_shares");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnType("char(36)");
        builder.Property(s => s.MindMapId).HasColumnType("char(36)");
        builder.Property(s => s.CreatedById).HasColumnType("char(36)");

        builder.Property(s => s.ShareToken).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Password).HasMaxLength(64);
        builder.Property(s => s.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(s => s.ExpiresAt).HasColumnType("datetime(3)");
        builder.Property(s => s.LastAccessedAt).HasColumnType("datetime(3)");

        builder.HasIndex(s => s.ShareToken).IsUnique();
        builder.HasIndex(s => new { s.MindMapId, s.CreatedAt });

        builder.HasOne(s => s.MindMap)
            .WithMany()
            .HasForeignKey(s => s.MindMapId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
