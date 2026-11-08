using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Carts;

/// <summary>
/// 修改购物车条目数量的请求数据
/// </summary>
public class UpdateCartItemQuantityRequest
{
    // 修改后的最终数量，不是本次增加的数量
    [Required(ErrorMessage = "请输入购买数量")]
    [Range(1, int.MaxValue, ErrorMessage = "购买数量必须大于 0")]
    public int? Quantity { get; set; }
}