using System.Globalization;
using Aop.Api.Response;
using ECP.Api.Constants.Orders;
using ECP.Api.Constants.Payments;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;

namespace ECP.Api.Services.Payments;

/// <summary>
/// 支付宝查询结果核对后的处理状态
/// </summary>
public enum AlipayQueryState
{
    // 支付宝尚未创建交易
    NotCreated = 0,

    // 交易已创建，等待买家付款
    Waiting = 1,

    // 已确认付款成功
    Succeeded = 2,

    // 已确认渠道交易关闭
    Closed = 3,

    // 渠道未返回有效查询结果
    Unavailable = 4,

    // 返回信息与本地记录不一致
    Mismatch = 5
}

/// <summary>
/// 主动查询和关单共用的支付宝结果核对及状态更新逻辑
/// </summary>
public static class AlipayPaymentStateProcessor
{
    // 调用方先锁定订单，本方法只修改内存中的实体，不保存或提交事务
    public static AlipayQueryState Apply(
        Order order,
        Payment payment,
        AlipayTradeQueryResponse response)
    {
        if (response.Code == "40004"
            && response.SubCode == "ACQ.TRADE_NOT_EXIST")
        {
            return AlipayQueryState.NotCreated;
        }

        if (response.Code != "10000")
        {
            return AlipayQueryState.Unavailable;
        }

        // 所有信息核对通过后，才允许修改实体
        if (response.OutTradeNo != payment.PaymentNo
            || string.IsNullOrWhiteSpace(response.TradeNo)
            || !decimal.TryParse(
                response.TotalAmount,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal channelAmount)
            || channelAmount != payment.Amount
            || channelAmount != order.PayableAmount
            || (!string.IsNullOrEmpty(payment.ProviderTransactionId)
                && payment.ProviderTransactionId != response.TradeNo))
        {
            return AlipayQueryState.Mismatch;
        }

        bool paid = response.TradeStatus == "TRADE_SUCCESS"
            || response.TradeStatus == "TRADE_FINISHED";
        bool closed = response.TradeStatus == "TRADE_CLOSED";

        if (!paid && !closed && response.TradeStatus != "WAIT_BUYER_PAY")
        {
            return AlipayQueryState.Mismatch;
        }

        DateTime? paidAtUtc = null;

        // 渠道时间为选填项，提供时转换为 UTC
        if (paid && !string.IsNullOrWhiteSpace(response.SendPayDate))
        {
            if (!DateTimeOffset.TryParseExact(
                response.SendPayDate + " +08:00",
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset channelPaidAt))
            {
                return AlipayQueryState.Mismatch;
            }

            paidAtUtc = channelPaidAt.UtcDateTime;
        }

        payment.ProviderTransactionId = response.TradeNo;

        if (paid)
        {
            // 确认渠道实际付款结果，不因当前时间超过本地期限而拒绝入账
            payment.Status = PaymentStatuses.Succeeded;
            payment.PaidAtUtc = paidAtUtc;
            order.Status = OrderStatuses.Paid;
            order.PaidAtUtc = paidAtUtc;
            return AlipayQueryState.Succeeded;
        }

        if (closed)
        {
            // 这里只关闭支付尝试，订单和库存由关单服务处理
            payment.Status = PaymentStatuses.Closed;
            return AlipayQueryState.Closed;
        }

        return AlipayQueryState.Waiting;
    }
}
