using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员设置商品上下架状态的请求数据
/// </summary>
public class UpdateProductSaleStatusRequest
{
    // 必须明确提交 true 或 false，避免遗漏字段时误操作
    [Required(ErrorMessage = "请明确指定商品是否上架")]
    public bool? IsOnSale { get; set; }
}