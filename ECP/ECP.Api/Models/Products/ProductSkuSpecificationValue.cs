namespace ECP.Api.Models.Products;

/// <summary>
/// SKU 与规格值的关联实体，记录一个 SKU 对某种规格选择的具体值
/// </summary>
public class ProductSkuSpecificationValue
{
    // 所属 SKU 编号
    public int SkuId { get; set; }

    // 所选规格编号
    public int SpecificationId { get; set; }

    // 所选规格值编号
    public int SpecificationValueId { get; set; }
}