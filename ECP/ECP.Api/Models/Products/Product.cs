namespace ECP.Api.Models.Products;

/// <summary>
/// 商品实体，保存各个 SKU 共用的商品信息
/// </summary>
public class Product
{
    // 商品编号
    public int Id { get; set; }

    // 所属分类编号
    public int CategoryId { get; set; }

    // 商品名称
    public string Name { get; set; } = string.Empty;

    // 商品描述，默认空字符串
    public string Description { get; set; } = string.Empty;

    // 是否上架，新商品默认不上架
    public bool IsOnSale { get; set; }

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}