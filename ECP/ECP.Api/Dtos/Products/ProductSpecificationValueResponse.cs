namespace ECP.Api.Dtos.Products;

/// <summary>
/// 商品规格值返回数据
/// </summary>
public class ProductSpecificationValueResponse
{
    // 规格值编号
    public int Id { get; set; }

    // 所属规格编号
    public int SpecificationId { get; set; }

    // 规格值内容，例如黑色、256GB
    public string Value { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    public int SortOrder { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}