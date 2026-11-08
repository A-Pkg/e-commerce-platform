namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 创建订单或重复提交时返回的订单基本信息
/// </summary>
public class CreateOrderResponse
{
    // 订单内部编号
    public int Id { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 当前订单状态
    public string Status { get; set; } = string.Empty;

    // 下单时确定的商品金额
    public decimal ItemsAmount { get; set; }

    // 下单时确定的运费
    public decimal ShippingFee { get; set; }

    // 下单时确定的应付金额
    public decimal PayableAmount { get; set; }

    // 下单时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }

    // 付款截止时间，使用 UTC
    public DateTime ExpiresAtUtc { get; set; }
}