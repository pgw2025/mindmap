using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnType("char(36)");
        builder.Property(t => t.UserId).HasColumnType("char(36)");

        builder.Property(t => t.Name).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Color).HasMaxLength(16).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("datetime(3)");

        // 同一用户下标签名唯一
        builder.HasIndex(t => new { t.UserId, t.Name }).IsUnique();

        builder.HasOne(t => t.User)
            .WithMany(u => u.Tags)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
