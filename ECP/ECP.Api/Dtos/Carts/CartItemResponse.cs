namespace ECP.Api.Dtos.Carts;

/// <summary>
/// 当前用户的购物车条目，包含商品、规格和当前可购买状态
/// </summary>
public class CartItemResponse
{
    // 购物车条目编号
    public int Id { get; set; }

    // 商品编号
    public int ProductId { get; set; }

    // 商品名称
    public string ProductName { get; set; } = string.Empty;

    // SKU 编号
    public int SkuId { get; set; }

    // SKU 业务编码
    public string SkuCode { get; set; } = string.Empty;

    // 规格展示文字，没有选择规格时为空
    public string SpecificationText { get; set; } = string.Empty;

    // SKU 当前售价，不是下单成交价
    public decimal Price { get; set; }

    // SKU 当前库存
    public int Stock { get; set; }

    // 购物车中的购买数量
    public int Quantity { get; set; }

    // 是否勾选
    public bool IsSelected { get; set; }

    // 当前商品状态及库存是否允许购买该数量
    public bool IsAvailable { get; set; }

    // 不可购买原因，可购买时为空
    public string UnavailableReason { get; set; } = string.Empty;
}