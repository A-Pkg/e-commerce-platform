using ECP.Api.Constants.Payments;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECP.Api.Data.Configurations.Payments;

/// <summary>
/// 支付记录的数据库映射配置
/// </summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    // 配置支付表的字段、检查约束、订单关联和索引
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        // 配置数据库表名及支付状态、金额约束
        builder.ToTable("payments", tableBuilder =>
        {
            // 数据库只允许保存已定义的支付状态
            tableBuilder.HasCheckConstraint(
                "CK_payments_status",
                $"`Status` IN ('{PaymentStatuses.Pending}', '{PaymentStatuses.Succeeded}', " +
                $"'{PaymentStatuses.Failed}', '{PaymentStatuses.Closed}')");

            // 支付金额不能为负数
            tableBuilder.HasCheckConstraint(
                "CK_payments_amount_nonnegative",
                "`Amount` >= 0");
        });

        // 支付记录内部编号作为主键
        builder.HasKey(payment => payment.Id);

        // 支付记录内部编号由数据库自动生成
        builder.Property(payment => payment.Id)
            .ValueGeneratedOnAdd();

        // 每条支付记录必须有所属订单
        builder.Property(payment => payment.OrderId)
            .IsRequired();

        // 系统支付业务编号必填，最多 64 个字符
        builder.Property(payment => payment.PaymentNo)
            .HasMaxLength(64)
            .IsRequired();

        // 支付渠道必填，最多 20 个字符，数据库默认使用模拟支付
        builder.Property(payment => payment.Provider)
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PaymentProviders.Mock);

        // 渠道交易编号允许为空，最多 64 个字符
        builder.Property(payment => payment.ProviderTransactionId)
            .HasMaxLength(64)
            .IsRequired(false);

        // 支付状态必填，最多 32 个字符，数据库默认待支付
        builder.Property(payment => payment.Status)
            .HasMaxLength(32)
            .IsRequired()
            .HasDefaultValue(PaymentStatuses.Pending);

        // 支付金额必填，使用 decimal(18,2)
        builder.Property(payment => payment.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        // 创建时间必填，保留微秒精度
        builder.Property(payment => payment.CreatedAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired();

        // 支付成功时间允许为空，保留微秒精度
        builder.Property(payment => payment.PaidAtUtc)
            .HasColumnType("datetime(6)")
            .IsRequired(false);

        // 一个订单允许有多次支付尝试，存在支付记录时禁止直接删除订单
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(payment => payment.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // 系统支付业务编号全站唯一
        builder.HasIndex(payment => payment.PaymentNo)
            .IsUnique();

        // 创建唯一索引字段组合，同一渠道的交易编号不能重复
        builder.HasIndex(payment => new
        {
            payment.Provider,
            payment.ProviderTransactionId
        })
            .IsUnique();

        // 创建查询索引字段组合，支持查询订单的支付尝试并按时间、编号排列
        builder.HasIndex(payment => new
        {
            payment.OrderId,
            payment.CreatedAtUtc,
            payment.Id
        });
    }
}