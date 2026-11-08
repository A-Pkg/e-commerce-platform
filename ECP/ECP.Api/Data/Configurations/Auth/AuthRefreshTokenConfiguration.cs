using ECP.Api.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Auth;

/// <summary>
/// 刷新令牌表映射
/// </summary>
public class AuthRefreshTokenConfiguration
    : IEntityTypeConfiguration<AuthRefreshToken>
{
    public void Configure(EntityTypeBuilder<AuthRefreshToken> builder)
    {
        builder.ToTable("auth_refresh_tokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedOnAdd();

        builder.Property(token => token.SessionId)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        builder.HasIndex(token => token.SessionId);

        builder.Property(token => token.CreatedAtUtc)
            .HasColumnType("datetime(6)");

        builder.Property(token => token.UsedAtUtc)
            .HasColumnType("datetime(6)");

        builder.HasOne<AuthSession>()
            .WithMany()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}