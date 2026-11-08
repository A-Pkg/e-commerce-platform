using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 提交订单的请求数据，不接收用户编号、库存或应付总额
/// </summary>
public class CreateOrderRequest
{
    // 前端为一次下单生成的请求标识，同次重试复用
    // 格式沿用现有 SKU 保存请求的 32 位十六进制标识
    [Required(ErrorMessage = "缺少下单请求标识")]
    [RegularExpression(
        "^[a-fA-F0-9]{32}$",
        ErrorMessage = "下单请求标识必须为 32 位十六进制字符")]
    public string RequestId { get; set; } = string.Empty;

    // 用户在结算页面选择的本人地址编号
    [Range(1, int.MaxValue, ErrorMessage = "请选择收货地址")]
    public int AddressId { get; set; }

    // 创建商品确认列表，内容来自本次结算预览
    [Required(ErrorMessage = "缺少结算商品")]
    [MinLength(1, ErrorMessage = "请至少选择一项结算商品")]
    public List<CreateOrderItemRequest> Items { get; set; } =
        new List<CreateOrderItemRequest>();
}