using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Products;

/// <summary>
/// 管理员创建商品 SKU 的请求数据
/// </summary>
public class CreateProductSkuRequest
{
    // 售价必填，沿用现有数据库的非负金额约束
    [Required(ErrorMessage = "请输入 SKU 售价")]
    [Range(
        typeof(decimal),
        "0",
        "9999999999999999.99",
        ErrorMessage = "售价必须在 0 到 9999999999999999.99 之间")]
    public decimal? Price { get; set; }

    // 初始库存，未填写时默认为 0
    [Range(
        0,
        int.MaxValue,
        ErrorMessage = "初始库存必须为 0 到 2147483647 之间的整数")]
    public int InitialStock { get; set; }

    // 创建所选规格值编号列表，允许为空列表，但不能显式传入 null
    [Required(ErrorMessage = "规格值列表不能为 null")]
    public List<int> SpecificationValueIds { get; set; }
        = new List<int>();
}