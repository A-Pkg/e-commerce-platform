using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Users;

/// <summary>
/// 用户收货地址的数据库映射配置
/// </summary>
public class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    // 配置地址表的字段、用户关联和查询索引
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        // 数据库表名
        builder.ToTable("user_addresses");

        // 地址编号作为主键
        builder.HasKey(address => address.Id);

        // 地址编号由数据库自动生成
        builder.Property(address => address.Id)
            .ValueGeneratedOnAdd();

        // 每条地址必须有所属用户
        builder.Property(address => address.UserId)
            .IsRequired();

        // 收货人姓名必填，最多 50 个字符
        builder.Property(address => address.RecipientName)
            .HasMaxLength(50)
            .IsRequired();

        // 联系电话必填，最多 32 个字符
        builder.Property(address => address.Phone)
            .HasMaxLength(32)
            .IsRequired();

        // 完整收货地址必填，最多 500 个字符
        builder.Property(address => address.Address)
            .HasMaxLength(500)
            .IsRequired();

        // 创建时间必填，保留微秒精度
        builder.Property(address => address.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 一个用户可以有多条地址，存在地址关联时禁止直接物理删除用户
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(address => address.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 创建索引字段组合，支持按用户查询地址并按编号排列
        builder.HasIndex(address => new
        {
            address.UserId,
            address.Id
        });
    }
}