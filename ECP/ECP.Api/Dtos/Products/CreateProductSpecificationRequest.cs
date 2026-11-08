using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员新增商品规格的请求数据
/// </summary>
public class CreateProductSpecificationRequest
{
    // 规格名称，必填，最多 50 个字符
    [Required(ErrorMessage = "请输入规格名称")]
    [StringLength(50, ErrorMessage = "规格名称不能超过 50 个字符")]
    public string Name { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    [Range(0, int.MaxValue, ErrorMessage = "排序值不能小于 0")]
    public int SortOrder { get; set; }
}