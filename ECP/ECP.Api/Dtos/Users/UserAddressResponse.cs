namespace ECP.Api.Dtos.Users;

/// <summary>
/// 当前登录用户的收货地址信息
/// </summary>
public class UserAddressResponse
{
    // 地址编号
    public int Id { get; set; }

    // 收货人姓名
    public string RecipientName { get; set; } = string.Empty;

    // 联系电话
    public string Phone { get; set; } = string.Empty;

    // 完整收货地址
    public string Address { get; set; } = string.Empty;

    // 地址创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}