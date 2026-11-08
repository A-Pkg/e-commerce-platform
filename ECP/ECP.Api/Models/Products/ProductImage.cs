namespace ECP.Api.Models.Products;

/// <summary>
/// 商品图片实体，保存图片访问地址和展示顺序
/// </summary>
public class ProductImage
{
    // 图片记录编号
    public int Id { get; set; }

    // 所属商品编号
    public int ProductId { get; set; }

    // 图片访问地址
    public string ImageUrl { get; set; } = string.Empty;

    // 展示顺序，数值越小越靠前
    public int SortOrder { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}