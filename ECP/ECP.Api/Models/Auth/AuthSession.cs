namespace ECP.Api.Models.Auth;

/// <summary>
/// 一次登录产生的会话，刷新令牌不会改变会话的截止时间
/// </summary>
public class AuthSession
{
    // 会话编号，同时写入访问令牌的 sid 声明
    public string Id { get; set; } = string.Empty;

    // 所属用户编号
    public int UserId { get; set; }

    // 登录时的认证版本，改密后与用户当前版本不一致
    public int TokenVersion { get; set; }

    // 本次登录的 UTC 时间
    public DateTime CreatedAtUtc { get; set; }

    // 本次登录的固定截止时间
    public DateTime ExpiresAtUtc { get; set; }

    // 主动退出或检测到刷新令牌重复使用时记录
    public DateTime? RevokedAtUtc { get; set; }
}