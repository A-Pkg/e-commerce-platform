using ECP.Api.Models.Carts;
using ECP.Api.Models.Products;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Carts;

/// <summary>
/// 购物车条目的数据库映射配置
/// </summary>
public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    // 配置购物车表的字段、外键、数量约束和唯一索引
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        // 配置数据库表名和购买数量约束
        builder.ToTable("cart_items", tableBuilder =>
        {
            // 数据库要求购买数量必须大于零
            tableBuilder.HasCheckConstraint(
                "CK_cart_items_quantity_positive",
                "`Quantity` > 0");
        });

        // 购物车条目编号作为主键
        builder.HasKey(item => item.Id);

        // 条目编号由数据库自动生成
        builder.Property(item => item.Id)
            .ValueGeneratedOnAdd();

        // 每条购物车记录必须有所属用户
        builder.Property(item => item.UserId)
            .IsRequired();

        // 每条购物车记录必须关联具体 SKU
        builder.Property(item => item.SkuId)
            .IsRequired();

        // 购买数量必填
        builder.Property(item => item.Quantity)
            .IsRequired();

        // 勾选状态必填，数据库默认勾选
        builder.Property(item => item.IsSelected)
            .IsRequired()
            .HasDefaultValue(true);

        // 创建时间必填，保留微秒精度
        builder.Property(item => item.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 关联所属用户，存在购物车记录时禁止直接物理删除用户
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 关联具体 SKU，存在购物车记录时禁止直接删除该 SKU
        builder.HasOne<ProductSku>()
            .WithMany()
            .HasForeignKey(item => item.SkuId)
            .OnDelete(DeleteBehavior.Restrict);

        // 创建唯一索引字段组合，同一用户对同一 SKU 只保存一条记录
        builder.HasIndex(item => new
        {
            item.UserId,
            item.SkuId
        })
            .IsUnique();
    }
}