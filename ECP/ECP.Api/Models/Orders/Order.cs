using ECP.Api.Constants.Orders;

namespace ECP.Api.Models.Orders;

/// <summary>
/// 订单实体，保存交易主体、金额、收货快照、物流信息和状态时间
/// </summary>
public class Order
{
    // 订单内部编号
    public int Id { get; set; }

    // 对外订单号，由后续下单业务生成，全站唯一
    public string OrderNo { get; set; } = string.Empty;

    // 所属用户编号，从当前登录身份中取得
    public int UserId { get; set; }

    // 下单请求的幂等标识，同一次结算的重复提交必须复用
    public string RequestId { get; set; } = string.Empty;

    // 当前订单状态，新订单默认待支付
    public string Status { get; set; } = OrderStatuses.PendingPayment;

    // 商品金额，由后端汇总订单明细的小计
    public decimal ItemsAmount { get; set; }

    // 运费，由后续下单业务按运费规则计算
    public decimal ShippingFee { get; set; }

    // 应付金额，等于商品金额加运费
    public decimal PayableAmount { get; set; }

    // 下单时复制的收货人姓名快照
    public string RecipientName { get; set; } = string.Empty;

    // 下单时复制的收货人电话快照
    public string RecipientPhone { get; set; } = string.Empty;

    // 下单时复制的完整收货地址快照
    public string ShippingAddress { get; set; } = string.Empty;

    // 用户备注，允许为空
    public string? Remark { get; set; }

    // 物流公司，发货时填写
    public string? ShippingCompany { get; set; }

    // 物流运单号，发货时由管理员填写
    public string? TrackingNumber { get; set; }

    // 下单时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // 支付截止时间，由后续下单业务按支付有效期设置
    public DateTime ExpiresAtUtc { get; set; }

    // 支付成功时间，尚未支付时为空
    public DateTime? PaidAtUtc { get; set; }

    // 发货时间，尚未发货时为空
    public DateTime? ShippedAtUtc { get; set; }

    // 确认收货时间，尚未完成时为空
    public DateTime? CompletedAtUtc { get; set; }

    // 取消或超时关闭时间，尚未取消时为空
    public DateTime? CancelledAtUtc { get; set; }
}