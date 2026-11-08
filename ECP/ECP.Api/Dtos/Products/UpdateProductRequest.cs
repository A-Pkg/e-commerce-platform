using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员修改商品公共信息的请求数据
/// </summary>
public class UpdateProductRequest
{
    // 所属分类编号，必须为正整数
    [Range(1, int.MaxValue, ErrorMessage = "请选择有效的商品分类")]
    public int CategoryId { get; set; }

    // 商品名称，必填，最多 200 个字符
    [Required(ErrorMessage = "请输入商品名称")]
    [StringLength(200, ErrorMessage = "商品名称不能超过 200 个字符")]
    public string Name { get; set; } = string.Empty;

    // 商品描述，允许空字符串，最多 2000 个字符
    [StringLength(2000, ErrorMessage = "商品描述不能超过 2000 个字符")]
    public string Description { get; set; } = string.Empty;
}