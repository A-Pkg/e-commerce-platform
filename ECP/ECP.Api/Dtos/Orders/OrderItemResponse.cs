namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 订单商品明细，返回下单时保存的信息，不包含图片
/// </summary>
public class OrderItemResponse
{
    // 订单明细编号
    public int Id { get; set; }

    // 商品编号
    public int ProductId { get; set; }

    // 下单时保存的商品名称
    public string ProductName { get; set; } = string.Empty;

    // SKU 编号
    public int SkuId { get; set; }

    // 下单时保存的 SKU 业务编码
    public string SkuCode { get; set; } = string.Empty;

    // 下单时保存的规格文字，无规格时为空
    public string SpecificationText { get; set; } = string.Empty;

    // 成交单价
    public decimal UnitPrice { get; set; }

    // 购买数量
    public int Quantity { get; set; }

    // 成交小计
    public decimal Subtotal { get; set; }
}