namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 管理员订单列表中的一条订单
/// </summary>
public class AdminOrderListItemResponse
{
    // 订单内部编号，用于查询详情
    public int Id { get; set; }

    // 对外订单号
    public string OrderNo { get; set; } = string.Empty;

    // 下单用户的账号
    public string Username { get; set; } = string.Empty;

    // 当前订单状态
    public string Status { get; set; } = string.Empty;

    // 商品总件数，汇总各条订单明细的购买数量
    public int TotalQuantity { get; set; }

    // 整单应付金额，包含运费
    public decimal PayableAmount { get; set; }

    // 下单时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}