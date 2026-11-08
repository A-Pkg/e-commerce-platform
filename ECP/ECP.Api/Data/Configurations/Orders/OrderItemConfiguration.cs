using ECP.Api.Models.Orders;
using ECP.Api.Models.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Orders;

/// <summary>
/// 订单明细的数据库映射配置
/// </summary>
public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    // 配置订单明细表的字段、检查约束、外键和唯一索引
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        // 配置数据库表名及单价、数量和小计约束
        builder.ToTable("order_items", tableBuilder =>
        {
            // 成交单价不能为负数
            tableBuilder.HasCheckConstraint(
                "CK_order_items_unit_price_nonnegative",
                "`UnitPrice` >= 0");

            // 购买数量必须大于零
            tableBuilder.HasCheckConstraint(
                "CK_order_items_quantity_positive",
                "`Quantity` > 0");

            // 明细小计必须等于成交单价乘购买数量
            tableBuilder.HasCheckConstraint(
                "CK_order_items_subtotal",
                "`Subtotal` = `UnitPrice` * `Quantity`");
        });

        // 订单明细编号作为主键
        builder.HasKey(item => item.Id);

        // 订单明细编号由数据库自动生成
        builder.Property(item => item.Id)
            .ValueGeneratedOnAdd();

        // 每条明细必须有所属订单
        builder.Property(item => item.OrderId)
            .IsRequired();

        // 每条明细必须关联商品
        builder.Property(item => item.ProductId)
            .IsRequired();

        // 每条明细必须关联具体 SKU
        builder.Property(item => item.SkuId)
            .IsRequired();

        // 商品名称快照必填，最多 200 个字符
        builder.Property(item => item.ProductName)
            .HasMaxLength(200)
            .IsRequired();

        // SKU 编码快照必填，最多 64 个字符
        builder.Property(item => item.SkuCode)
            .HasMaxLength(64)
            .IsRequired();

        // 规格描述快照必填，最多 1000 个字符
        builder.Property(item => item.SpecificationText)
            .HasMaxLength(1000)
            .IsRequired();

        // 商品图片地址允许为空，最多 1024 个字符
        builder.Property(item => item.ProductImageUrl)
            .HasMaxLength(1024)
            .IsRequired(false);

        // 成交单价必填，使用 decimal(18,2)
        builder.Property(item => item.UnitPrice)
            .HasPrecision(18, 2)
            .IsRequired();

        // 购买数量必填
        builder.Property(item => item.Quantity)
            .IsRequired();

        // 明细小计必填，使用 decimal(18,2)
        builder.Property(item => item.Subtotal)
            .HasPrecision(18, 2)
            .IsRequired();

        // 创建时间必填，保留微秒精度
        builder.Property(item => item.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 关联所属订单，存在明细时禁止直接删除订单
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // 关联商品，存在历史订单明细时禁止直接删除商品
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // 关联 SKU，存在历史订单明细时禁止直接删除 SKU
        builder.HasOne<ProductSku>()
            .WithMany()
            .HasForeignKey(item => item.SkuId)
            .OnDelete(DeleteBehavior.Restrict);

        // 创建唯一索引字段组合，一个订单中同一 SKU 只保存一条明细
        builder.HasIndex(item => new
        {
            item.OrderId,
            item.SkuId
        })
            .IsUnique();
    }
}