namespace ECP.Api.Dtos.Products;

/// <summary>
/// 商品规格返回数据，包含该规格下的规格值列表
/// </summary>
public class ProductSpecificationResponse
{
    // 规格编号
    public int Id { get; set; }

    // 所属商品编号
    public int ProductId { get; set; }

    // 规格名称，例如颜色、容量
    public string Name { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    public int SortOrder { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; }

    // 创建规格值列表，没有规格值时返回空数组
    public List<ProductSpecificationValueResponse> Values { get; set; }
        = new List<ProductSpecificationValueResponse>();
}