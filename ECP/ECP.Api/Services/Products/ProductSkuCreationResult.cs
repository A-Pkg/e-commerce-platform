using ECP.Api.Dtos.Products;

namespace ECP.Api.Services.Products;

/// <summary>
/// SKU 创建失败的业务原因
/// </summary>
public enum ProductSkuCreationFailure
{
    // 没有错误
    None = 0,

    // 请求参数无效
    InvalidInput = 1,

    // 商品不存在
    ProductNotFound = 2,

    // 操作用户不存在
    OperatorNotFound = 3,

    // 同一商品已存在相同规格组合
    DuplicateCombination = 4,

    // 保存期间关联数据发生变化
    RelatedDataChanged = 5,

    // SKU 编码冲突重试后仍未成功
    SkuCodeConflict = 6
}

/// <summary>
/// SKU 创建服务的返回结果
/// </summary>
public sealed class ProductSkuCreationResult
{
    // 创建成功后返回的 SKU
    public ProductSkuResponse? Sku { get; init; }

    // 失败原因，成功时为 None
    public ProductSkuCreationFailure Failure { get; init; }

    // 失败时向管理员说明原因
    public string Message { get; init; } = string.Empty;

    // 判断本次创建是否成功
    public bool Succeeded =>
        Failure == ProductSkuCreationFailure.None && Sku is not null;

    // 创建成功结果
    public static ProductSkuCreationResult Success(ProductSkuResponse sku)
    {
        // 创建包含实际 SKU 数据的结果
        return new ProductSkuCreationResult
        {
            Sku = sku,
            Failure = ProductSkuCreationFailure.None
        };
    }

    // 创建失败结果
    public static ProductSkuCreationResult Fail(
        ProductSkuCreationFailure failure,
        string message)
    {
        // 创建包含业务失败原因的结果
        return new ProductSkuCreationResult
        {
            Failure = failure,
            Message = message
        };
    }
}