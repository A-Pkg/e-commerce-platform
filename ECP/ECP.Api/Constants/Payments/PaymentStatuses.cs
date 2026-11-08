namespace ECP.Api.Constants.Payments;

/// <summary>
/// 支付状态常量，统一支付记录的状态值
/// </summary>
public static class PaymentStatuses
{
    // 待支付
    public const string Pending = "Pending";

    // 支付成功
    public const string Succeeded = "Succeeded";

    // 支付失败
    public const string Failed = "Failed";

    // 支付关闭
    public const string Closed = "Closed";
}