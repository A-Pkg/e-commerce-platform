namespace ECP.Api.Dtos.Payments;

/// <summary>
/// 模拟付款成功或重复付款时返回的订单和支付信息
/// </summary>
public class MockPaymentResponse
{
    // 订单内部编号
    public int OrderId { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 当前订单状态
    public string OrderStatus { get; set; } = string.Empty;

    // 系统支付业务编号
    public string PaymentNo { get; set; } = string.Empty;

    // 支付渠道，当前为 Mock
    public string Provider { get; set; } = string.Empty;

    // 支付记录状态
    public string PaymentStatus { get; set; } = string.Empty;

    // 实际支付金额，取订单中已经确定的应付金额
    public decimal Amount { get; set; }

    // 支付成功时间，使用 UTC
    public DateTime? PaidAtUtc { get; set; }
}