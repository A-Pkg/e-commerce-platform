namespace ECP.Api.Dtos.Payments;

/// <summary>
/// 支付结果查询响应，返回本系统已经确认的订单和支付状态
/// </summary>
public class QueryOrderPaymentResponse
{
    // 订单内部编号
    public int OrderId { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 当前订单状态
    public string OrderStatus { get; set; } = string.Empty;

    // 本系统支付编号
    public string PaymentNo { get; set; } = string.Empty;

    // 支付渠道
    public string Provider { get; set; } = string.Empty;

    // 本系统已经确认的支付状态
    public string PaymentStatus { get; set; } = string.Empty;

    // 本次支付金额
    public decimal Amount { get; set; }

    // 渠道返回的时间，转换为 UTC；渠道未提供时为空
    public DateTime? PaidAtUtc { get; set; }

    // 本次查询结果的提示
    public string Message { get; set; } = string.Empty;
}