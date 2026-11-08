namespace ECP.Api.Dtos.Users;

/// <summary>
/// 管理员查询用户时返回的基本信息
/// </summary>
public class AdminUserResponse
{
    // 用户编号
    public int Id { get; set; }

    // 登录账号
    public string Username { get; set; } = string.Empty;

    // 用户角色
    public string Role { get; set; } = string.Empty;

    // 账号是否启用
    public bool IsActive { get; set; }

    // 注册时间，使用 UTC
    public DateTime CreatedAtUtc { get; set; }
}