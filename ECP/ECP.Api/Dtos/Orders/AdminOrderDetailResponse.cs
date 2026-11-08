namespace ECP.Api.Dtos.Orders;

/// <summary>
/// 管理员订单详情，复用已有详情字段，补充用户账号、物流和履约时间
/// </summary>
public class AdminOrderDetailResponse : OrderDetailResponse
{
    // 下单用户的账号
    public string Username { get; set; } = string.Empty;

    // 下单时保存的用户备注
    public string? Remark { get; set; }

    // 物流公司，未填写时为空
    public string? ShippingCompany { get; set; }

    // 物流运单号，未填写时为空
    public string? TrackingNumber { get; set; }
}