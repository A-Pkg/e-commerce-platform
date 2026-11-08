namespace ECP.Api.Constants.Stocks;

/// <summary>
/// 库存变化原因常量，统一库存流水的原因存储值
/// </summary>
public static class StockMovementReasons
{
    // 初始入库
    public const string InitialStock = "InitialStock";

    // 下单扣减
    public const string OrderDeduction = "OrderDeduction";

    // 取消归还，包含未支付订单超时关闭后的库存归还
    public const string OrderCancellation = "OrderCancellation";

    // 后台调整
    public const string AdminAdjustment = "AdminAdjustment";
}