namespace ECP.Api.Dtos.Auth;

/// <summary>
/// 令牌响应 DTO，用于封装访问令牌及其相关信息
/// </summary>
public class AccessTokenResponse
{
    // 访问令牌
    public string AccessToken { get; set; } = string.Empty;

    // 客户端携带令牌时使用的认证类型
    public string TokenType { get; set; } = "Bearer";

    // 令牌过期时间，使用 UTC
    public DateTime ExpiresAtUtc { get; set; }

    // 同一次登录保持不变，前端用它区分续期与更换账号
    public string SessionId { get; set; } = string.Empty;

    // 登录会话的固定截止时间
    public DateTime SessionExpiresAtUtc { get; set; }
}