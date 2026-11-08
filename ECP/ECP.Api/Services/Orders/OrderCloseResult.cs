namespace ECP.Api.Services.Orders;

/// <summary>
/// 关闭订单的业务处理结果，仅在后端代码中使用
/// </summary>
public enum OrderCloseResult
{
    // 本次成功关闭订单并归还库存
    Closed = 0,

    // 订单已经关闭，无需重复处理
    AlreadyClosed = 1,

    // 未找到符合条件的订单
    NotFound = 2,

    // 当前订单不是待支付状态
    NotPendingPayment = 3,

    // 超时检查发现订单尚未到期
    NotExpired = 4,

    // 库存归还未能完成，事务已经回滚
    StockUpdateFailed = 5,

    // 数据库锁冲突，本次未完成处理
    Busy = 6,

    // 关单过程中确认渠道已付款，已保存付款结果
    PaymentConfirmed = 7,

    // 渠道仍可付款或表单尚未到期，取消尚未完成
    PaymentPending = 8,

    // 渠道查询或关单结果暂时无法确认
    PaymentUnavailable = 9,

    // 渠道返回信息未通过核对
    PaymentDataMismatch = 10
}