namespace ECP.Api.Models.Products;

/// <summary>
/// 商品规格实体，保存具体商品的规格名称，例如颜色、容量
/// </summary>
public class ProductSpecification
{
    // 规格编号
    public int Id { get; set; }

    // 所属商品编号
    public int ProductId { get; set; }

    // 规格名称
    public string Name { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    public int SortOrder { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}