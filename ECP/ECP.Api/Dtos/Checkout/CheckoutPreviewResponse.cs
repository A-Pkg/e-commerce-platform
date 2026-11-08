namespace ECP.Api.Dtos.Checkout;

/// <summary>
/// 结算预览响应，包含商品清单和后端计算的金额
/// </summary>
public class CheckoutPreviewResponse
{
    // 本次结算的商品条目，创建空列表作为默认值
    public List<CheckoutPreviewItemResponse> Items { get; set; } =
        new List<CheckoutPreviewItemResponse>();

    // 商品总额，等于所有商品小计之和
    public decimal ItemsAmount { get; set; }

    // 运费，本项目固定为零
    public decimal ShippingFee { get; set; }

    // 应付金额，等于商品总额加运费
    public decimal PayableAmount { get; set; }
}