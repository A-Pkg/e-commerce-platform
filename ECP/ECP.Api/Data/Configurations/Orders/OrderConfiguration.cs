using ECP.Api.Constants.Orders;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Orders;

/// <summary>
/// 订单的数据库映射配置
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    // 配置订单表的字段、检查约束、用户关联和索引
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // 配置数据库表名及状态、金额检查约束
        builder.ToTable("orders", tableBuilder =>
        {
            // 数据库只允许保存已定义的订单状态
            tableBuilder.HasCheckConstraint(
                "CK_orders_status",
                $"`Status` IN ('{OrderStatuses.PendingPayment}', '{OrderStatuses.Paid}', " +
                $"'{OrderStatuses.Shipped}', '{OrderStatuses.Completed}', '{OrderStatuses.Cancelled}')");

            // 商品金额、运费和应付金额均不能为负数
            tableBuilder.HasCheckConstraint(
                "CK_orders_amounts_nonnegative",
                "`ItemsAmount` >= 0 AND `ShippingFee` >= 0 AND `PayableAmount` >= 0");

            // 应付金额必须等于商品金额加运费
            tableBuilder.HasCheckConstraint(
                "CK_orders_payable_amount",
                "`PayableAmount` = `ItemsAmount` + `ShippingFee`");
        });

        // 订单内部编号作为主键
        builder.HasKey(order => order.Id);

        // 订单内部编号由数据库自动生成
        builder.Property(order => order.Id)
            .ValueGeneratedOnAdd();

        // 对外订单号必填，最多 64 个字符
        builder.Property(order => order.OrderNo)
            .HasMaxLength(64)
            .IsRequired();

        // 每个订单必须有所属用户
        builder.Property(order => order.UserId)
            .IsRequired();

        // 下单请求幂等标识必填，最多 64 个字符
        builder.Property(order => order.RequestId)
            .HasMaxLength(64)
            .IsRequired();

        // 订单状态必填，数据库默认待支付
        builder.Property(order => order.Status)
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(OrderStatuses.PendingPayment);

        // 商品金额必填，使用 decimal(18,2)
        builder.Property(order => order.ItemsAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        // 运费必填，使用 decimal(18,2)，数据库默认值为 0
        builder.Property(order => order.ShippingFee)
            .HasPrecision(18, 2)
            .IsRequired()
            .HasDefaultValue(0m);

        // 应付金额必填，使用 decimal(18,2)
        builder.Property(order => order.PayableAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        // 收货人姓名快照必填，最多 50 个字符
        builder.Property(order => order.RecipientName)
            .HasMaxLength(50)
            .IsRequired();

        // 收货人电话快照必填，最多 32 个字符
        builder.Property(order => order.RecipientPhone)
            .HasMaxLength(32)
            .IsRequired();

        // 完整收货地址快照必填，最多 500 个字符
        builder.Property(order => order.ShippingAddress)
            .HasMaxLength(500)
            .IsRequired();

        // 用户备注允许为空，最多 500 个字符
        builder.Property(order => order.Remark)
            .HasMaxLength(500)
            .IsRequired(false);

        // 物流公司允许为空，最多 100 个字符
        builder.Property(order => order.ShippingCompany)
            .HasMaxLength(100)
            .IsRequired(false);

        // 物流运单号允许为空，最多 64 个字符
        builder.Property(order => order.TrackingNumber)
            .HasMaxLength(64)
            .IsRequired(false);

        // 下单时间必填，保留微秒精度
        builder.Property(order => order.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 支付截止时间必填，保留微秒精度
        builder.Property(order => order.ExpiresAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 支付时间允许为空，保留微秒精度
        builder.Property(order => order.PaidAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 发货时间允许为空，保留微秒精度
        builder.Property(order => order.ShippedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 完成时间允许为空，保留微秒精度
        builder.Property(order => order.CompletedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 取消时间允许为空，保留微秒精度
        builder.Property(order => order.CancelledAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 一个用户可以有多个订单，存在订单时禁止直接物理删除用户
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 对外订单号全站唯一
        builder.HasIndex(order => order.OrderNo)
            .IsUnique();

        // 创建唯一索引字段组合，同一用户的同一次下单请求只能对应一个订单
        builder.HasIndex(order => new
        {
            order.UserId,
            order.RequestId
        })
            .IsUnique();

        // 创建查询索引字段组合，支持查询用户订单并按创建时间、编号排列
        builder.HasIndex(order => new
        {
            order.UserId,
            order.CreatedAtUtc,
            order.Id
        });

        // 创建查询索引字段组合，支持按状态查找已到支付截止时间的订单
        builder.HasIndex(order => new
        {
            order.Status,
            order.ExpiresAtUtc,
            order.Id
        });
    }
}