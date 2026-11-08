using ECP.Api.Dtos.Payments;

namespace ECP.Api.Services.Payments;

/// <summary>
/// 订单支付结果查询服务的返回结果
/// </summary>
public sealed class OrderPaymentQueryResult
{
    // 查询成功后返回的订单和支付信息
    public QueryOrderPaymentResponse? Payment { get; init; }

    // 业务失败原因，成功时为 None
    public OrderPaymentFailure Failure { get; init; }

    // 失败提示
    public string Message { get; init; } = string.Empty;

    // 查询成功表示取得有效结果，不等于订单已经付款
    public bool Succeeded =>
        Failure == OrderPaymentFailure.None && Payment is not null;

    // 创建查询成功结果
    public static OrderPaymentQueryResult Success(
        QueryOrderPaymentResponse payment)
    {
        // 创建包含当前支付信息的结果
        return new OrderPaymentQueryResult
        {
            Payment = payment,
            Failure = OrderPaymentFailure.None
        };
    }

    // 创建业务失败结果
    public static OrderPaymentQueryResult Fail(
        OrderPaymentFailure failure,
        string message)
    {
        // 创建包含失败原因的结果
        return new OrderPaymentQueryResult
        {
            Failure = failure,
            Message = message
        };
    }
}