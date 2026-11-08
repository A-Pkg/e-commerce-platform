namespace ECP.Api.Dtos.Products;

/// <summary>
/// 商品 SKU 返回数据，包含规格展示文字、价格和库存
/// </summary>
public class ProductSkuResponse
{
    // SKU 内部编号
    public int Id { get; set; }

    // 所属商品编号
    public int ProductId { get; set; }

    // SKU 业务编码
    public string SkuCode { get; set; } = string.Empty;

    // 规格组合展示文字，例如“颜色：黑色 / 容量：256GB”
    public string SpecificationText { get; set; } = string.Empty;

    // 当前售价，单位为人民币元
    public decimal Price { get; set; }

    // 当前可售库存
    public int Stock { get; set; }

    // 是否启用
    public bool IsActive { get; set; }
}