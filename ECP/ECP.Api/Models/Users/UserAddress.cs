namespace ECP.Api.Models.Users;

/// <summary>
/// 用户收货地址实体，保存收货人、联系电话和完整地址
/// </summary>
public class UserAddress
{
    // 地址编号
    public int Id { get; set; }

    // 所属用户编号
    public int UserId { get; set; }

    // 收货人姓名
    public string RecipientName { get; set; } = string.Empty;

    // 联系电话，使用字符串保存
    public string Phone { get; set; } = string.Empty;

    // 完整收货地址
    public string Address { get; set; } = string.Empty;

    // 创建时间，统一使用 UTC
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}