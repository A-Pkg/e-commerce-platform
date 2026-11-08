using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员修改商品 SKU 启用状态的请求数据
/// </summary>
public class UpdateProductSkuStatusRequest
{
    // 必须明确传入 true 或 false，避免遗漏字段时误操作为停用
    [Required(ErrorMessage = "请明确指定 SKU 是否启用")]
    public bool? IsActive { get; set; }
}