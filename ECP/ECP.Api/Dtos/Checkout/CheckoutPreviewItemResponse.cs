namespace ECP.Api.Dtos.Checkout;

/// <summary>
/// 结算预览中的商品条目，价格和数量均由后端读取
/// </summary>
public class CheckoutPreviewItemResponse
{
    // 购物车条目编号
    public int CartItemId { get; set; }

    // 商品编号
    public int ProductId { get; set; }

    // 商品名称
    public string ProductName { get; set; } = string.Empty;

    // SKU 编号
    public int SkuId { get; set; }

    // SKU 业务编码
    public string SkuCode { get; set; } = string.Empty;

    // 规格展示文字，无规格时为空
    public string SpecificationText { get; set; } = string.Empty;

    // 当前单价，预览时尚未锁定成交价格
    public decimal UnitPrice { get; set; }

    // 购物车中已保存的购买数量
    public int Quantity { get; set; }

    // 商品小计，等于当前单价乘购买数量
    public decimal Subtotal { get; set; }
}