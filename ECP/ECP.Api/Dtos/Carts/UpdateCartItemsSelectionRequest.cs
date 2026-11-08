using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Carts;

/// <summary>
/// 批量设置购物车条目勾选状态的请求数据
/// </summary>
public class UpdateCartItemsSelectionRequest
{
    // 本次需要设置状态的购物车条目编号
    [Required(ErrorMessage = "请提交购物车条目编号列表")]
    [MinLength(1, ErrorMessage = "请至少选择一个购物车条目")]
    public List<int>? CartItemIds { get; set; }

    // 明确指定选中或取消，不使用状态取反
    [Required(ErrorMessage = "请明确指定是否勾选")]
    public bool? IsSelected { get; set; }
}