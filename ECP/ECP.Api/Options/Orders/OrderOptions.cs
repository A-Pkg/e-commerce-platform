namespace ECP.Api.Options.Orders;

/// <summary>
/// 订单业务配置
/// </summary>
public class OrderOptions
{
    // 待支付订单的付款期限，单位为分钟，默认 30 分钟
    public int PaymentTimeoutMinutes { get; set; } = 30;
}