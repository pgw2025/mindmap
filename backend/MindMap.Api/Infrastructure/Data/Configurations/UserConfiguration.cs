using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MindMap.Api.Domain.Entities;

namespace MindMap.Api.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnType("char(36)");

        builder.Property(u => u.Username)
            .HasMaxLength(32)
            .UseCollation("utf8mb4_bin") // 大小写敏感
            .IsRequired();

        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.Email)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(u => u.PasswordSalt)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(u => u.Avatar).HasMaxLength(512);

        builder.Property(u => u.Status).HasConversion<int>();

        builder.Property(u => u.LastLoginAt).HasColumnType("datetime(3)");
        builder.Property(u => u.CreatedAt).HasColumnType("datetime(3)");
        builder.Property(u => u.UpdatedAt).HasColumnType("datetime(3)");

        // 初始管理员种子：用户名 admin / 密码 Admin@2026
        // 注意：密码 hash/salt 在迁移初始化时通过 SQL 注入或种子工具生成。
        // 这里仅占位，密码字段将由首次启动时的初始化逻辑或手工创建。
        // 出于演示便利，预留字段：在 AuthService 首次启动如不存在管理员则创建。
    }
}
