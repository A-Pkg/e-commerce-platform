using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 提交订单时携带的商品确认信息，用于核对结算页面是否已经过期
/// </summary>
public class CreateOrderItemRequest
{
    // 结算预览返回的购物车条目编号
    [Range(1, int.MaxValue, ErrorMessage = "购物车条目编号无效")]
    public int CartItemId { get; set; }

    // 结算页面展示的购买数量
    [Range(1, int.MaxValue, ErrorMessage = "购买数量必须大于零")]
    public int Quantity { get; set; }

    // 结算页面展示的单价，仅用于核对，实际成交价仍由后端读取
    [Required(ErrorMessage = "缺少商品确认单价")]
    [Range(
        typeof(decimal),
        "0",
        "9999999999999999.99",
        ErrorMessage = "商品确认单价超出允许范围")]
    public decimal? UnitPrice { get; set; }
}