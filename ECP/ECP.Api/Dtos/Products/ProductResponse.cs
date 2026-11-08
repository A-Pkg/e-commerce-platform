namespace ECP.Api.Dtos.Products;

/// <summary>
/// 商品公共信息响应，供后台商品查询使用
/// </summary>
public class ProductResponse
{
    // 商品编号
    public int Id { get; set; }

    // 所属分类编号
    public int CategoryId { get; set; }

    // 所属分类名称
    public string CategoryName { get; set; } = string.Empty;

    // 商品名称
    public string Name { get; set; } = string.Empty;

    // 商品描述
    public string Description { get; set; } = string.Empty;

    // 是否上架
    public bool IsOnSale { get; set; }

    // 商品创建时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}