namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 本人订单列表中的一条订单，包含基本信息和商品快照
/// </summary>
public class OrderListItemResponse
{
    // 订单内部编号，用于打开订单详情
    public int Id { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 当前订单状态
    public string Status { get; set; } = string.Empty;

    // 整单应付金额，已包含运费
    public decimal PayableAmount { get; set; }

    // 下单时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }

    // 付款截止时间，使用 UTC，供列表判断付款入口文案
    public DateTime ExpiresAtUtc { get; set; }

    // 创建商品明细列表，保存下单时的商品信息
    public List<OrderItemResponse> Items { get; set; } =
        new List<OrderItemResponse>();
}