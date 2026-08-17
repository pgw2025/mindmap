using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnType("char(36)");
        builder.Property(t => t.UserId).HasColumnType("char(36)");

        builder.Property(t => t.TokenHash)
            .HasMaxLength(88)
            .IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(88);
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.Property(t => t.ExpiresAt).HasColumnType("datetime(3)");
        builder.Property(t => t.RevokedAt).HasColumnType("datetime(3)");
        builder.Property(t => t.CreatedAt).HasColumnType("datetime(3)");

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
