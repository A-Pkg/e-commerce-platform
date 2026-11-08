namespace ECP.Api.Models.Auth;

/// <summary>
/// 刷新令牌记录，保留已使用记录以识别重复使用
/// </summary>
public class AuthRefreshToken
{
    // 数据库生成的记录编号
    public long Id { get; set; }

    // 所属登录会话
    public string SessionId { get; set; } = string.Empty;

    // 仅保存随机令牌的 SHA-256 哈希
    public string TokenHash { get; set; } = string.Empty;

    // 本次令牌签发时间
    public DateTime CreatedAtUtc { get; set; }

    // 使用后不删除记录，而是标记使用时间
    public DateTime? UsedAtUtc { get; set; }
}