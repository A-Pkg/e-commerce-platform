namespace ECP.Api.Models.Products;

/// <summary>
/// 商品 SKU 实体，保存具体销售版本的编码、价格和可售库存
/// </summary>
public class ProductSku
{
    // SKU 内部编号
    public int Id { get; set; }

    // 所属商品编号
    public int ProductId { get; set; }

    // SKU 业务编码，由后续业务生成，全站唯一
    public string SkuCode { get; set; } = string.Empty;

    // 规格组合摘要，由后续业务校验并计算
    public string SpecificationKey { get; set; } = string.Empty;

    // 当前售价，单位为人民币元
    public decimal Price { get; set; }

    // 当前可售库存
    public int Stock { get; set; }

    // 是否启用，新 SKU 默认启用
    public bool IsActive { get; set; } = true;

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}