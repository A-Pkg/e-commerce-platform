using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Auth;
using ECP.Api.Dtos.Users;
using ECP.Api.Models.Users;
using ECP.Api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace ECP.Api.Controllers.Users;

/// <summary>
/// 提供注册、登录、改密、刷新、退出和当前用户查询接口
/// </summary>
[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 密码哈希服务，用于生成和验证密码哈希
    private readonly IPasswordHasher<AppUser> _passwordHasher;

    // 当前浏览器使用的刷新 Cookie 名称
    private const string RefreshCookieName = "ecp.refresh";

    // 登录会话和刷新令牌服务
    private readonly AuthSessionService _authSessionService;

    // 初始化数据库上下文、密码哈希服务和登录会话服务
    public AuthController(
        AppDbContext db,
        IPasswordHasher<AppUser> passwordHasher,
        AuthSessionService authSessionService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _authSessionService = authSessionService;
    }

    #region 用户注册
    // 验证注册信息，创建新用户并保存密码哈希
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        // 检查账号是否已存在
        bool usernameExists = await _db.Users.AnyAsync(
            user => user.Username == request.Username,cancellationToken);

        if (usernameExists)
        {
            return Conflict(new
            {
                message = "该账号已存在"
            });
        }

        // 创建用户实体
        AppUser newUser = new AppUser
        {
            Username = request.Username,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 将密码转换为哈希，数据库不保存明文密码
        newUser.PasswordHash = _passwordHasher.HashPassword(newUser,request.Password);

        _db.Users.Add(newUser);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 两个请求同时注册相同账号时，
            // 由数据库唯一索引阻止重复，并返回友好提示
            return Conflict(new
            {
                message = "该账号已存在"
            });
        }

        // 只返回需要的信息，不返回密码哈希
        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "注册成功",
            userId = newUser.Id,
            username = newUser.Username
        });
    }
    #endregion

    #region 用户登录
    // 验证账号密码，创建登录会话，返回访问令牌并写入刷新 Cookie
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        // 根据账号查询用户
        AppUser? user = await _db.Users.SingleOrDefaultAsync(
            user => user.Username == request.Username,cancellationToken);

        if (user is null)
        {
            return Unauthorized(new
            {
                message = "账号或密码错误"
            });
        }

        // 验证输入密码与数据库中的密码哈希是否匹配
        PasswordVerificationResult verificationResult =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

        // 密码错误时统一返回账号或密码错误
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                message = "账号或密码错误"
            });
        }

        // 密码正确后检查账号状态，停用账号不签发令牌
        if (!user.IsActive)
        {
            return Unauthorized(new
            {
                message = "账号已停用，请联系管理员"
            });
        }

        // 如果已有哈希需要升级，使用当前配置重新生成并保存
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            try
            {
                // TokenVersion 是并发标记，防止覆盖其他请求刚修改的新密码
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Unauthorized(new
                {
                    message = "账号信息已发生变化，请重新登录"
                });
            }
        }

        // 密码验证及哈希升级完成后，创建会话并签发访问令牌和刷新令牌
        AuthSessionResult sessionResult = await _authSessionService.CreateAsync(
            user,
            Request.Cookies[RefreshCookieName],
            cancellationToken);

        WriteRefreshCookie(sessionResult);

        AccessTokenResponse tokenResponse = sessionResult.Token;

        return Ok(new
        {
            message = "登录成功",
            userId = user.Id,
            username = user.Username,
            accessToken = tokenResponse.AccessToken,
            tokenType = tokenResponse.TokenType,
            expiresAtUtc = tokenResponse.ExpiresAtUtc,
            sessionId = tokenResponse.SessionId,
            sessionExpiresAtUtc = tokenResponse.SessionExpiresAtUtc
        });
    }
    #endregion


    #region 修改密码
    // 验证原密码，保存新密码哈希并使该账号的全部旧令牌失效
    [Authorize(Roles = UserRoles.User)]
    [EnableRateLimiting("auth")]
    [HttpPut("password")]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        // 用户编号从已经验证的登录身份读取，不接受客户端指定
        string? userIdValue = User.FindFirstValue(
            JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            || userId <= 0)
        {
            return Unauthorized();
        }

        // 与认证事件保持一致：缺失版本声明时按零解析，仍须通过会话校验
        string? tokenVersionValue = User.FindFirstValue("token_version");

        int tokenVersion = 0;

        if (tokenVersionValue is not null
            && (!int.TryParse(
                    tokenVersionValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out tokenVersion)
                || tokenVersion < 0))
        {
            return Unauthorized();
        }

        // 查询当前账号及密码哈希，后续使用条件更新统一保存
        AppUser? user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == userId,
                cancellationToken);

        // 再次检查版本，避免认证完成后、进入方法前账号已经改密
        if (user is null
            || !user.IsActive
            || user.TokenVersion != tokenVersion)
        {
            return Unauthorized();
        }

        // 同时核对数据库中的角色，本接口只服务普通用户
        if (user.Role != UserRoles.User)
        {
            return Forbid();
        }

        // 验证原密码，不对密码进行 Trim 或其他转换
        PasswordVerificationResult verificationResult =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.CurrentPassword);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            // 原密码错误属于本次操作失败，不清除有效登录
            return BadRequest(new
            {
                message = "原密码错误"
            });
        }

        // 已确认规则：新密码不能与原密码相同
        if (string.Equals(
                request.CurrentPassword,
                request.NewPassword,
                StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                message = "新密码不能与原密码相同"
            });
        }

        // 保存读取时的哈希，用于核对提交期间是否被其他请求修改
        string originalPasswordHash = user.PasswordHash;

        // 使用现有密码哈希服务生成新哈希，数据库不保存明文密码
        string newPasswordHash = _passwordHasher.HashPassword(
            user,
            request.NewPassword);

        // 密码哈希与版本在同一条 SQL 中更新
        // 若账号被停用或其他请求先完成改密，本次更新不会覆盖其结果
        int affectedRows = await _db.Users
            .Where(item => item.Id == userId
                && item.Role == UserRoles.User
                && item.IsActive
                && item.TokenVersion == tokenVersion
                && item.PasswordHash == originalPasswordHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.PasswordHash,
                        newPasswordHash)
                    .SetProperty(
                        item => item.TokenVersion,
                        item => item.TokenVersion + 1),
                cancellationToken);

        if (affectedRows == 0)
        {
            return Conflict(new
            {
                message = "账号状态或密码已发生变化，请重新登录后重试"
            });
        }

        // 认证版本已递增，其他会话的访问与刷新都会被拒绝
        DeleteRefreshCookie();

        // 条件更新已经直接保存数据库，无需再次 SaveChangesAsync
        // 当前令牌也已失效，前端收到成功后应清除会话并返回登录页
        return NoContent();
    }

    #endregion

    #region 会话刷新与退出

    // 登录、刷新和退出之前获取防伪令牌；这些请求不携带 Bearer
    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken(
        [FromServices] IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens =
            antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new
        {
            requestToken = tokens.RequestToken
        });
    }

    // 不要求有效访问令牌，使用 Cookie 中的刷新令牌验证身份
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(
        CancellationToken cancellationToken)
    {
        string? refreshToken = Request.Cookies[RefreshCookieName];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new
            {
                message = "登录已失效，请重新登录"
            });
        }

        AuthSessionResult? result = await _authSessionService.RefreshAsync(
            refreshToken,
            cancellationToken);

        if (result is null)
        {
            DeleteRefreshCookie();

            return Unauthorized(new
            {
                message = "登录已失效，请重新登录"
            });
        }

        WriteRefreshCookie(result);

        return Ok(result.Token);
    }

    // 即使访问令牌已过期，也可以主动结束当前浏览器的会话
    [AllowAnonymous]
    [EnableRateLimiting("auth-session")]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        await _authSessionService.RevokeAsync(
            Request.Cookies[RefreshCookieName],
            cancellationToken);

        DeleteRefreshCookie();

        return NoContent();
    }

    // 刷新令牌仅写入 Cookie，不放入 JSON 或路由
    private void WriteRefreshCookie(AuthSessionResult result)
    {
        Response.Cookies.Append(
            RefreshCookieName,
            result.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth",
                Expires = new DateTimeOffset(
                    result.Token.SessionExpiresAtUtc)
            });
    }

    // 删除时保持名称、路径等配置一致
    private void DeleteRefreshCookie()
    {
        Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth"
            });
    }

    #endregion


    #region 获取当前用户
    // 根据已经验证的令牌，查询并返回当前用户
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        // 从当前身份的 sub 声明中读取用户编号
        string? userIdValue = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        // 检查用户编号是否为有效的正整数
        if (!int.TryParse(userIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out int userId) || userId <= 0)
        {
            return Unauthorized();
        }

        // 查询当前用户，只读查询不需要跟踪实体变化
        AppUser? user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

        // 用户可能在令牌签发后被删除，此时不再返回用户信息
        if (user is null)
        {
            return Unauthorized();
        }

        // 创建响应对象，返回用户编号、账号和角色
        CurrentUserResponse response = new CurrentUserResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Role = user.Role
        };

        // 返回当前用户信息
        return Ok(response);
    }
    #endregion
}