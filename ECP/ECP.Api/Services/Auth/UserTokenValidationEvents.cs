using ECP.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;

namespace ECP.Api.Services.Auth;

/// <summary>
/// JWT 基本校验通过后，检查登录会话、账号启用状态及认证版本
/// </summary>
public class UserTokenValidationEvents : JwtBearerEvents
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public UserTokenValidationEvents(AppDbContext db)
    {
        _db = db;
    }

    // 每次认证时检查会话和用户，拒绝已撤销、已到期、停用或改密前的登录
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        string? userIdValue = context.Principal?
            .FindFirst(JwtRegisteredClaimNames.Sub)?
            .Value;

        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            context.Fail("登录身份无效");
            return;
        }

        string? tokenVersionValue = context.Principal?
            .FindFirst("token_version")?
            .Value;

        // 缺失版本声明时按零解析；这不代表兼容所有旧令牌
        // 没有 sid 或有效会话记录的令牌，仍会被下面的查询拒绝
        int tokenVersion = 0;

        if (tokenVersionValue is not null
            && (!int.TryParse(
                    tokenVersionValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out tokenVersion)
                || tokenVersion < 0))
        {
            context.Fail("登录身份无效");
            return;
        }

        string? sessionId = context.Principal?
            .FindFirst("sid")?
            .Value;

        DateTime nowUtc = DateTime.UtcNow;

        // 同时验证会话与用户；退出后，未到期的 JWT 也不能再使用
        bool userIsValid = await _db.AuthSessions
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == sessionId
                    && session.UserId == userId
                    && session.TokenVersion == tokenVersion
                    && session.RevokedAtUtc == null
                    && session.ExpiresAtUtc > nowUtc
                    && _db.Users.Any(user =>
                        user.Id == userId
                        && user.IsActive
                        && user.TokenVersion == tokenVersion),
                context.HttpContext.RequestAborted);

        if (!userIsValid)
        {
            context.Fail("账号不存在、已停用或登录已失效");
        }
    }

    // 标明访问令牌过期或其他认证失败
    // access_token_expired 不代表会话仍有效，能否续期由刷新接口继续判断
    public override async Task Challenge(
        JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        context.Response.Headers.WWWAuthenticate = "Bearer";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsJsonAsync(
            new
            {
                code = context.AuthenticateFailure
                    is SecurityTokenExpiredException
                        ? "access_token_expired"
                        : "unauthorized",

                message = "登录已失效，请重新登录"
            },
            context.HttpContext.RequestAborted);
    }
}