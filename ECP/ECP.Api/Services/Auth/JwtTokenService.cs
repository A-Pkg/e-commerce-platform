using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ECP.Api.Dtos.Auth;
using ECP.Api.Models.Users;
using ECP.Api.Options.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECP.Api.Services.Auth;

/// <summary>
/// JWT令牌生成服务
/// </summary>
public class JwtTokenService
{
    // JWT配置
    private readonly JwtOptions _jwtOptions;
    // 令牌签名凭据，包含签名密钥和算法
    private readonly SigningCredentials _signingCredentials;

    // 初始化JWT配置和签名凭据
    public JwtTokenService(IOptions<JwtOptions> jwtOptions)
    {
        // 获取已经绑定并通过校验的配置
        _jwtOptions = jwtOptions.Value;

        // 将 Base64 文本还原为原始密钥字节
        byte[] keyBytes = Convert.FromBase64String(_jwtOptions.SigningKeyBase64);

        // 创建一个对称密钥对象，用于签名
        SymmetricSecurityKey signingKey = new SymmetricSecurityKey(keyBytes);

        // 使用 HS256 算法签名
        _signingCredentials = new SigningCredentials(signingKey,SecurityAlgorithms.HmacSha256);
    }


    // 登录及续期时签发访问令牌
    public AccessTokenResponse CreateToken(
        AppUser user,
        string sessionId,
        DateTime sessionExpiresAtUtc)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = nowUtc.AddMinutes(_jwtOptions.ExpiresMinutes);

        // MySQL 读取的 DateTime 可能没有 Kind，业务上统一按 UTC 处理
        DateTime sessionEndUtc = DateTime.SpecifyKind(
            sessionExpiresAtUtc,
            DateTimeKind.Utc);

        // 访问令牌不能超过整个登录会话的截止时间
        if (expiresAtUtc > sessionEndUtc)
        {
            expiresAtUtc = sessionEndUtc;
        }

        Claim[] claims = new Claim[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString(CultureInfo.InvariantCulture)),

            new Claim(
                JwtRegisteredClaimNames.UniqueName,
                user.Username),

            new Claim("role", user.Role),

            new Claim(
                "token_version",
                user.TokenVersion.ToString(CultureInfo.InvariantCulture)),

            // 同一会话续期时 sid 不变
            new Claim("sid", sessionId),

            // 每张访问令牌的编号不同
            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N"))
        };

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expiresAtUtc,
            signingCredentials: _signingCredentials);

        JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();

        return new AccessTokenResponse
        {
            AccessToken = tokenHandler.WriteToken(token),
            ExpiresAtUtc = token.ValidTo,
            SessionId = sessionId,
            SessionExpiresAtUtc = sessionEndUtc
        };
    }
}