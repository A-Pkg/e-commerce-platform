using ECP.Api.Dtos.Payments;

namespace ECP.Api.Services.Payments;

/// <summary>
/// 订单支付业务失败的原因
/// </summary>
public enum OrderPaymentFailure
{
    // 没有错误
    None = 0,

    // 订单编号无效
    InvalidInput = 1,

    // 当前用户不存在或身份无效
    UserNotFound = 2,

    // 本人订单不存在
    OrderNotFound = 3,

    // 当前订单状态不支持付款
    NotPendingPayment = 4,

    // 订单已经超过付款期限
    Expired = 5,

    // 数据库锁冲突，本次未完成处理
    Busy = 6,

    // 尚未创建支付记录
    PaymentNotFound = 7,

    // 暂时无法取得有效的支付宝查询结果
    GatewayUnavailable = 8,

    // 渠道返回的交易信息无法通过核对
    GatewayDataMismatch = 9,

    // 查询期间订单或支付记录已变化
    StateChanged = 10
}

/// <summary>
/// 订单付款准备服务的返回结果
/// </summary>
public sealed class OrderPaymentPreparationResult
{
    // 成功时返回订单和支付信息
    public PrepareOrderPaymentResponse? Payment { get; init; }

    // 失败原因，成功时为 None
    public OrderPaymentFailure Failure { get; init; }

    // 失败时向用户说明原因
    public string Message { get; init; } = string.Empty;

    // 判断本次付款准备是否成功
    public bool Succeeded =>
        Failure == OrderPaymentFailure.None && Payment is not null;

    // 创建成功结果
    public static OrderPaymentPreparationResult Success(
        PrepareOrderPaymentResponse payment)
    {
        // 创建包含实际支付信息的结果
        return new OrderPaymentPreparationResult
        {
            Payment = payment,
            Failure = OrderPaymentFailure.None
        };
    }

    // 创建业务失败结果
    public static OrderPaymentPreparationResult Fail(
        OrderPaymentFailure failure,
        string message)
    {
        // 创建包含业务失败原因的结果
        return new OrderPaymentPreparationResult
        {
            Failure = failure,
            Message = message
        };
    }
}