using ECP.Api.Constants.Payments;

namespace ECP.Api.Models.Payments;

/// <summary>
/// 支付记录实体，保存某个订单的一次支付尝试及其结果
/// </summary>
public class Payment
{
    // 支付记录内部编号
    public int Id { get; set; }

    // 所属订单编号
    public int OrderId { get; set; }

    // 系统支付业务编号，由后续支付业务生成，全站唯一
    public string PaymentNo { get; set; } = string.Empty;

    // 支付渠道，默认使用模拟支付
    public string Provider { get; set; } = PaymentProviders.Mock;

    // 支付渠道交易编号，尚未取得时为空
    public string? ProviderTransactionId { get; set; }

    // 当前支付状态，新支付尝试默认待支付
    public string Status { get; set; } = PaymentStatuses.Pending;

    // 本次支付金额，由后端从订单应付金额取得
    public decimal Amount { get; set; }

    // 创建支付尝试的时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // 支付成功时间，尚未成功时为空
    public DateTime? PaidAtUtc { get; set; }
}