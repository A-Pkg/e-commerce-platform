using ECP.Api.Constants.Users;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Users;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // 数据库表名
        builder.ToTable("users");

        // 主键
        builder.HasKey(user => user.Id);

        // 用户编号由数据库生成
        builder.Property(user => user.Id)
            .ValueGeneratedOnAdd();

        // 账号必填，最多 50 个字符
        builder.Property(user => user.Username)
            .HasMaxLength(50)
            .IsRequired();

        // 账号不能重复
        builder.HasIndex(user => user.Username)
            .IsUnique();

        // 角色必填，最多 20 个字符，数据库默认值为普通用户
        builder.Property(user => user.Role)
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(UserRoles.User);

        // 账号启用状态必填，现有用户和新用户默认启用
        builder.Property(user => user.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // 密码哈希必填，最多 512 个字符
        builder.Property(user => user.PasswordHash)
            .HasMaxLength(512)
            .IsRequired();

        // 认证版本默认零，同时用于检测用户信息的并发修改
        builder.Property(user => user.TokenVersion)
            .IsRequired()
            .HasDefaultValue(0)
            .IsConcurrencyToken();

        // 伪删除状态必填，数据库默认未删除
        builder.Property(user => user.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        // 删除时间允许为空，保留微秒精度
        builder.Property(user => user.DeletedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 注册时间必填
        builder.Property(user => user.CreatedAtUtc)
            .IsRequired();
    }
}