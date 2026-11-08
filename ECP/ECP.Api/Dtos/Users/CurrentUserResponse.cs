namespace ECP.Api.Dtos.Users;

/// <summary>
/// 当前登录用户的基本信息
/// </summary>
public class CurrentUserResponse
{
    // 用户编号
    public int UserId { get; set; }

    // 登录账号
    public string Username { get; set; } = string.Empty;

    // 用户角色：User（普通用户）、Admin（管理员）
    public string Role { get; set; } = string.Empty;
}