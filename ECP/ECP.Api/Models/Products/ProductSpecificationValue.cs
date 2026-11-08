namespace ECP.Api.Models.Products;

/// <summary>
/// 商品规格值实体，保存某个规格下的可选值，例如黑色、256GB
/// </summary>
public class ProductSpecificationValue
{
    // 规格值编号
    public int Id { get; set; }

    // 所属规格编号
    public int SpecificationId { get; set; }

    // 规格值内容
    public string Value { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    public int SortOrder { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}