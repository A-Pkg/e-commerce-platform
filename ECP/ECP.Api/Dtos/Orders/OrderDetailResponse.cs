namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 本人订单详情，包含订单状态、成交金额、收货快照和商品明细
/// </summary>
public class OrderDetailResponse
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

    // 下单时保存的收货人
    public string RecipientName { get; set; } = string.Empty;

    // 下单时保存的联系电话
    public string RecipientPhone { get; set; } = string.Empty;

    // 下单时保存的完整收货地址
    public string ShippingAddress { get; set; } = string.Empty;

    // 下单时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }

    // 付款截止时间，使用 UTC
    public DateTime ExpiresAtUtc { get; set; }

    // 付款成功时间，未付款时为空
    public DateTime? PaidAtUtc { get; set; }

    // 发货时间，尚未发货时为空
    public DateTime? ShippedAtUtc { get; set; }

    // 确认收货时间，尚未完成时为空
    public DateTime? CompletedAtUtc { get; set; }

    // 取消时间，未取消时为空
    public DateTime? CancelledAtUtc { get; set; }

    // 创建商品明细列表，保存本订单的商品快照
    public List<OrderItemResponse> Items { get; set; } =
        new List<OrderItemResponse>();
}