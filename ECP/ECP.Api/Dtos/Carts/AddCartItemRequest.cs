using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Carts;

/// <summary>
/// 加入购物车的请求数据
/// </summary>
public class AddCartItemRequest
{
    // 具体销售规格的 SKU 编号，不是商品编号
    [Required(ErrorMessage = "请选择商品规格")]
    [Range(1, int.MaxValue, ErrorMessage = "SKU 编号必须大于 0")]
    public int? SkuId { get; set; }

    // 本次加入购物车的数量
    [Required(ErrorMessage = "请输入购买数量")]
    [Range(1, int.MaxValue, ErrorMessage = "购买数量必须大于 0")]
    public int? Quantity { get; set; }
}