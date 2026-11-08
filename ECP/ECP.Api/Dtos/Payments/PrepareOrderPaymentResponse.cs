namespace ECP.Api.Dtos.Payments;

/// <summary>
/// 付款准备结果，包含支付信息和可选的收银台表单
/// </summary>
public class PrepareOrderPaymentResponse
{
    // 订单内部编号
    public int OrderId { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 当前订单状态
    public string OrderStatus { get; set; } = string.Empty;

    // 本系统支付编号，重复准备付款时复用
    public string PaymentNo { get; set; } = string.Empty;

    // 本次支付渠道
    public string Provider { get; set; } = string.Empty;

    // 当前支付记录状态
    public string PaymentStatus { get; set; } = string.Empty;

    // 从订单读取的固定应付金额
    public decimal Amount { get; set; }

    // 原订单付款截止时间，使用 UTC
    public DateTime ExpiresAtUtc { get; set; }

    // 支付成功时间，尚未支付时为空
    public DateTime? PaidAtUtc { get; set; }

    // 支付宝收银台表单；已支付和零元订单返回空值
    public string? PaymentForm { get; set; }
}