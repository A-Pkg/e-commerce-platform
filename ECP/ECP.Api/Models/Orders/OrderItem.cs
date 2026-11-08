namespace ECP.Api.Models.Orders;

/// <summary>
/// 订单明细实体，保存所购 SKU 及下单时的商品、规格和成交价格快照
/// </summary>
public class OrderItem
{
    // 订单明细编号
    public int Id { get; set; }

    // 所属订单编号
    public int OrderId { get; set; }

    // 所属商品编号，从选定 SKU 的所属商品取得
    public int ProductId { get; set; }

    // 购买的 SKU 编号
    public int SkuId { get; set; }

    // 下单时复制的商品名称快照
    public string ProductName { get; set; } = string.Empty;

    // 下单时复制的 SKU 业务编码快照
    public string SkuCode { get; set; } = string.Empty;

    // 下单时复制的规格描述快照，例如颜色：黑色；容量：256GB
    public string SpecificationText { get; set; } = string.Empty;

    // 下单时复制的商品图片地址，允许为空
    public string? ProductImageUrl { get; set; }

    // 成交单价，由后端读取下单时的 SKU 售价
    public decimal UnitPrice { get; set; }

    // 购买数量，必须大于零
    public int Quantity { get; set; }

    // 明细小计，由后端计算，等于成交单价乘购买数量
    public decimal Subtotal { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}