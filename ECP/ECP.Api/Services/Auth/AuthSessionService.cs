using System.Data;
using System.Security.Cryptography;
using System.Text;
using ECP.Api.Data;
using ECP.Api.Models.Auth;
using ECP.Api.Models.Users;
using ECP.Api.Options.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace ECP.Api.Services.Auth;

/// <summary>
/// 创建登录会话、轮换刷新令牌及撤销当前会话
/// </summary>
public class AuthSessionService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;

    public AuthSessionService(
        AppDbContext db,
        JwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
    }

    // 账号密码验证通过后创建会话，替换当前浏览器已有的会话
    public async Task<AuthSessionResult> CreateAsync(
        AppUser user,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        await RevokeAsync(previousRefreshToken, cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        AuthSession session = new AuthSession
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            TokenVersion = user.TokenVersion,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddDays(_jwtOptions.SessionDays)
        };

        _db.AuthSessions.Add(session);

        AuthSessionResult result = IssueTokens(user, session);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    // 只允许未使用过的刷新令牌成功换取一次新令牌
    public async Task<AuthSessionResult?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        string tokenHash = HashToken(refreshToken);

        string? sessionId = await _db.AuthRefreshTokens
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => token.SessionId)
            .SingleOrDefaultAsync(cancellationToken);

        if (sessionId is null)
        {
            return null;
        }

        await using IDbContextTransaction transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        // 对同一会话加行锁，将数据库中的刷新操作串行处理
        // 重复使用旧刷新令牌仍会撤销会话，前端还需要跨标签页排队
        List<AuthSession> sessions = await _db.AuthSessions
            .FromSqlInterpolated(
                $"SELECT * FROM `auth_sessions` WHERE `Id` = {sessionId} FOR UPDATE")
            .ToListAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        if (sessions.Count == 0
            || sessions[0].RevokedAtUtc.HasValue
            || sessions[0].ExpiresAtUtc <= nowUtc)
        {
            return null;
        }

        AuthSession session = sessions[0];

        // 获得锁后再读取，不能使用等待锁之前的使用状态
        AuthRefreshToken token = await _db.AuthRefreshTokens.SingleAsync(
            item => item.TokenHash == tokenHash,
            cancellationToken);

        AppUser? user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == session.UserId,
                cancellationToken);

        if (token.UsedAtUtc.HasValue
            || user is null
            || !user.IsActive
            || user.TokenVersion != session.TokenVersion)
        {
            // 重复使用、改密或停用后，结束整个会话
            session.RevokedAtUtc = nowUtc;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        // 旧刷新令牌标记为已使用，同时登记新的刷新令牌
        token.UsedAtUtc = nowUtc;

        AuthSessionResult result = IssueTokens(user, session);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    // 撤销 Cookie 所属的会话，不影响其他浏览器或设备
    public async Task RevokeAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        string tokenHash = HashToken(refreshToken);

        string? sessionId = await _db.AuthRefreshTokens
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => token.SessionId)
            .SingleOrDefaultAsync(cancellationToken);

        if (sessionId is null)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;

        // UPDATE 会取得行锁，与刷新事务互斥
        await _db.AuthSessions
            .Where(session => session.Id == sessionId
                && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.RevokedAtUtc,
                    (DateTime?)nowUtc),
                cancellationToken);
    }

    // 登录与续期共用：生成访问令牌，并将刷新令牌记录加入跟踪
    // 此方法不保存数据库，保存和提交事务由调用方负责
    private AuthSessionResult IssueTokens(
        AppUser user,
        AuthSession session)
    {
        string refreshToken = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        _db.AuthRefreshTokens.Add(new AuthRefreshToken
        {
            SessionId = session.Id,
            TokenHash = HashToken(refreshToken),
            CreatedAtUtc = DateTime.UtcNow
        });

        return new AuthSessionResult
        {
            Token = _jwtTokenService.CreateToken(
                user,
                session.Id,
                session.ExpiresAtUtc),

            RefreshToken = refreshToken
        };
    }

    // 刷新令牌具有足够的随机性，数据库只保存其哈希
    private static string HashToken(string token)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}