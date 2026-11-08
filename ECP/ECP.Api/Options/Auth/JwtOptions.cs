namespace ECP.Api.Options.Auth;

public class JwtOptions
{
    // 令牌发行方
    public string Issuer { get; set; } = string.Empty;

    // 令牌接收方
    public string Audience { get; set; } = string.Empty;

    // 令牌有效期，单位为分钟
    public int ExpiresMinutes { get; set; }

    // 登录会话最长有效天数，刷新时不延长
    public int SessionDays { get; set; } = 7;

    // Base64 格式的签名密钥
    public string SigningKeyBase64 { get; set; } = string.Empty;
}