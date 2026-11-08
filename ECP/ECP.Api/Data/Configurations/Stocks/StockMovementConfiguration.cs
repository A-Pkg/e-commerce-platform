using ECP.Api.Constants.Stocks;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Products;
using ECP.Api.Models.Stocks;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Stocks;

/// <summary>
/// 库存流水的数据库映射配置
/// </summary>
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    // 配置库存流水表的字段、检查约束、外键和索引
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        // 配置数据库表名及库存变化检查约束
        builder.ToTable("stock_movements", tableBuilder =>
        {
            // 库存变化数量不能为零
            tableBuilder.HasCheckConstraint(
                "CK_stock_movements_quantity_change",
                "`QuantityChange` <> 0");

            // 变动后的可售库存不能为负数
            tableBuilder.HasCheckConstraint(
                "CK_stock_movements_balance_nonnegative",
                "`BalanceAfter` >= 0");

            // 数据库只允许保存已定义的库存变化原因
            tableBuilder.HasCheckConstraint(
                "CK_stock_movements_reason",
                $"`Reason` IN ('{StockMovementReasons.InitialStock}', " +
                $"'{StockMovementReasons.OrderDeduction}', " +
                $"'{StockMovementReasons.OrderCancellation}', " +
                $"'{StockMovementReasons.AdminAdjustment}')");
        });

        // 库存流水内部编号作为主键
        builder.HasKey(movement => movement.Id);

        // 库存流水内部编号由数据库自动生成
        builder.Property(movement => movement.Id)
            .ValueGeneratedOnAdd();

        // 库存业务流水号必填，最多 64 个字符
        builder.Property(movement => movement.MovementNo)
            .HasMaxLength(64)
            .IsRequired();

        // 每条库存流水必须关联一个 SKU
        builder.Property(movement => movement.SkuId)
            .IsRequired();

        // 库存变化数量必填，正数增加，负数减少
        builder.Property(movement => movement.QuantityChange)
            .IsRequired();

        // 变动后的可售库存必填
        builder.Property(movement => movement.BalanceAfter)
            .IsRequired();

        // 库存变化原因必填，最多 32 个字符，由业务明确填写
        builder.Property(movement => movement.Reason)
            .HasMaxLength(32)
            .IsRequired();

        // 订单引起的库存变化关联订单，其他变化可为空
        builder.Property(movement => movement.OrderId)
            .IsRequired(false);

        // 人工操作关联当前用户，系统自动处理可为空
        builder.Property(movement => movement.OperatorUserId)
            .IsRequired(false);

        // 库存变化时间必填，保留微秒精度
        builder.Property(movement => movement.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 关联 SKU，存在库存流水时禁止直接删除 SKU
        builder.HasOne<ProductSku>()
            .WithMany()
            .HasForeignKey(movement => movement.SkuId)
            .OnDelete(DeleteBehavior.Restrict);

        // 关联订单，存在库存流水引用时禁止直接删除订单
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(movement => movement.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // 关联操作用户，存在库存流水引用时禁止直接物理删除用户
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(movement => movement.OperatorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 库存业务流水号全站唯一，防止同一库存事件重复记录
        builder.HasIndex(movement => movement.MovementNo)
            .IsUnique();

        // 创建查询索引字段组合，支持查询指定 SKU 的库存变化历史
        builder.HasIndex(movement => new
        {
            movement.SkuId,
            movement.CreatedAtUtc,
            movement.Id
        });

        // 创建查询索引字段组合，支持查询指定订单的库存变化记录
        builder.HasIndex(movement => new
        {
            movement.OrderId,
            movement.Id
        });
    }
}