using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员新增商品规格值的请求数据
/// </summary>
public class CreateProductSpecificationValueRequest
{
    // 规格值内容，必填，最多 100 个字符
    [Required(ErrorMessage = "请输入规格值")]
    [StringLength(100, ErrorMessage = "规格值不能超过 100 个字符")]
    public string Value { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    [Range(0, int.MaxValue, ErrorMessage = "排序值不能小于 0")]
    public int SortOrder { get; set; }
}