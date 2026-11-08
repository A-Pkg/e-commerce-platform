using ECP.Api.Models.Auth;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Auth;

/// <summary>
/// 登录会话表映射
/// </summary>
public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(session => session.Id);

        // 使用程序生成的 32 位 GUID 文本
        builder.Property(session => session.Id)
            .HasMaxLength(32)
            .ValueGeneratedNever();

        builder.Property(session => session.CreatedAtUtc)
            .HasColumnType("datetime(6)");

        builder.Property(session => session.ExpiresAtUtc)
            .HasColumnType("datetime(6)");

        builder.Property(session => session.RevokedAtUtc)
            .HasColumnType("datetime(6)");

        builder.HasIndex(session => session.UserId);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}