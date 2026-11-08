using ECP.Api.Options.Auth;
using ECP.Api.Services.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace ECP.Api.Extensions;

/// <summary>
/// 身份认证服务的注册扩展
/// </summary>
public static class AuthenticationServiceExtensions
{
    // 注册 JWT Bearer 认证、令牌校验规则和授权服务
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        // 认证事件使用本次请求作用域中的数据库上下文
        services.AddScoped<UserTokenValidationEvents>();

        // 将 Bearer 设置为默认认证方案
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // 使用已经绑定并校验的 JWT 配置，设置 Bearer 认证规则
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                // 获取本项目的 JWT 配置
                JwtOptions options = jwtOptions.Value;

                // 将 Base64 文本还原为签名密钥字节
                byte[] keyBytes = Convert.FromBase64String(options.SigningKeyBase64);

                // 创建对称密钥对象，用于验证令牌签名
                SymmetricSecurityKey signingKey = new SymmetricSecurityKey(keyBytes);

                // 保留 sub、unique_name 等原始声明名称
                bearerOptions.MapInboundClaims = false;

                // JWT 验证通过后，继续检查登录会话、账号状态及认证版本
                bearerOptions.EventsType = typeof(UserTokenValidationEvents);

                // 创建令牌校验规则
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    // 必须具有签名，并使用本项目的密钥进行验证
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    // 只接受 HS256 签名算法
                    ValidAlgorithms = new string[] { SecurityAlgorithms.HmacSha256 },

                    // 验证令牌发行方
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,

                    // 验证令牌接收方
                    ValidateAudience = true,
                    ValidAudience = options.Audience,

                    // 必须包含过期时间，并检查令牌有效期
                    RequireExpirationTime = true,
                    ValidateLifetime = true,

                    // 不额外延长令牌的有效时间
                    ClockSkew = TimeSpan.Zero,

                    // 使用账号声明作为当前身份的名称
                    NameClaimType = JwtRegisteredClaimNames.UniqueName,

                    // 使用 role 声明识别当前用户的角色
                    RoleClaimType = "role"
                };
            });

        // 注册授权服务，供后续需要登录的接口使用
        services.AddAuthorization();

        return services;
    }
}