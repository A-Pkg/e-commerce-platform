namespace ECP.Api.Constants.Orders;

/// <summary>
/// 订单状态常量，统一订单状态的存储值
/// </summary>
public static class OrderStatuses
{
    // 待支付
    public const string PendingPayment = "PendingPayment";

    // 已支付，等待发货
    public const string Paid = "Paid";

    // 已发货，等待确认收货
    public const string Shipped = "Shipped";

    // 已完成
    public const string Completed = "Completed";

    // 已取消，包含未支付订单超时关闭
    public const string Cancelled = "Cancelled";
}