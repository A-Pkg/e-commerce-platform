using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Carts;

/// <summary>
/// 修改购物车条目勾选状态的请求数据
/// </summary>
public class UpdateCartItemSelectionRequest
{
    // 明确提交选中或取消，遗漏字段不能当作取消
    [Required(ErrorMessage = "请明确指定是否勾选")]
    public bool? IsSelected { get; set; }
}