namespace ECP.Api.Models.Carts;

/// <summary>
/// 购物车条目实体，保存某个用户选购的一种 SKU 及其数量
/// </summary>
public class CartItem
{
    // 购物车条目编号
    public int Id { get; set; }

    // 所属用户编号
    public int UserId { get; set; }

    // 选购的 SKU 编号
    public int SkuId { get; set; }

    // 购买数量，必须大于零
    public int Quantity { get; set; }

    // 是否勾选参与结算，新条目默认勾选
    public bool IsSelected { get; set; } = true;

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}