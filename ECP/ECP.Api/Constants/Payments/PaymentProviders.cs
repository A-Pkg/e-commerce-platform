namespace ECP.Api.Constants.Payments;

/// <summary>
/// 支付渠道常量
/// </summary>
public static class PaymentProviders
{
    // 原有模拟支付渠道
    public const string Mock = "Mock";

    // 支付宝渠道，当前用于沙箱支付
    public const string Alipay = "Alipay";

    // 零元订单在本系统内完成支付
    public const string Local = "Local";
}