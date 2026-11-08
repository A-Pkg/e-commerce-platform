using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Common;
using ECP.Api.Dtos.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员用户查询接口
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = UserRoles.Admin)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class AdminUsersController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public AdminUsersController(AppDbContext db)
    {
        _db = db;
    }

    // 按账号和角色分页查询用户，最新注册的用户排在前面
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<AdminUserResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? username,
        [FromQuery] string? role,
        [FromQuery] int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        // 当前阶段每页固定返回最多 20 条用户
        const int pageSize = 5;

        // 从已验证身份读取当前管理员编号，不接受客户端指定
        string? userIdValue = User.FindFirstValue(
            JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int currentUserId)
            || currentUserId <= 0)
        {
            return Unauthorized();
        }

        if (pageNumber <= 0)
        {
            // 创建页码无效的响应
            return BadRequest(new
            {
                message = "页码必须大于零"
            });
        }

        // 未填写或纯空格表示不使用对应条件
        string searchUsername = username?.Trim() ?? string.Empty;
        string selectedRole = role?.Trim() ?? string.Empty;

        // 角色筛选只接受项目中已有的角色
        if (selectedRole.Length > 0
            && selectedRole != UserRoles.User
            && selectedRole != UserRoles.Admin)
        {
            // 创建角色无效的响应
            return BadRequest(new
            {
                message = "用户角色无效"
            });
        }

        // 在统计和分页之前排除自己，避免条数和页码出现偏差
        IQueryable<AdminUserResponse> query = _db.Users
            .AsNoTracking()
            .Where(user => user.Id != currentUserId)
            .Select(user => new AdminUserResponse
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedAtUtc = user.CreatedAtUtc
            });

        // 账号按包含关系查询
        if (searchUsername.Length > 0)
        {
            query = query.Where(user =>
                user.Username.Contains(searchUsername));
        }

        // 角色条件与账号条件同时生效
        if (selectedRole.Length > 0)
        {
            query = query.Where(user =>
                user.Role == selectedRole);
        }

        // 先统计全部匹配用户，再计算分页信息
        int totalCount = await query.CountAsync(cancellationToken);

        // 空结果仍保留第 1 页
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize));

        // 请求页码超过范围时，返回最后一个有效页
        int currentPageNumber = Math.Min(pageNumber, totalPages);
        int skipCount = (currentPageNumber - 1) * pageSize;

        // 先排序再分页，只读取当前页用户
        List<AdminUserResponse> users = await query
            .OrderByDescending(user => user.CreatedAtUtc)
            .ThenByDescending(user => user.Id)
            .Skip(skipCount)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // 数据库保存 UTC 时间，读取后补充 UTC 标记
        foreach (AdminUserResponse user in users)
        {
            user.CreatedAtUtc = DateTime.SpecifyKind(
                user.CreatedAtUtc,
                DateTimeKind.Utc);
        }

        // 创建共用分页响应
        PagedResponse<AdminUserResponse> response =
            new PagedResponse<AdminUserResponse>
            {
                PageNumber = currentPageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = users
            };

        return Ok(response);
    }

    // 查询指定用户的基本信息
    [HttpGet("{id:int}")]
    [ProducesResponseType(
        typeof(AdminUserResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            // 创建用户编号无效的响应
            return BadRequest(new
            {
                message = "用户编号必须大于零"
            });
        }

        // 按编号查询，并创建只包含基本信息的响应
        AdminUserResponse? response = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new AdminUserResponse
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedAtUtc = user.CreatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            // 创建用户不存在的响应
            return NotFound(new
            {
                message = "该用户不存在"
            });
        }

        // 补充注册时间的 UTC 标记
        response.CreatedAtUtc = DateTime.SpecifyKind(
            response.CreatedAtUtc,
            DateTimeKind.Utc);

        return Ok(response);
    }


    // 停用普通用户，管理员账号不允许通过此接口停用
    [HttpPost("{id:int}/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disable(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            // 创建用户编号无效的响应
            return BadRequest(new
            {
                message = "用户编号必须大于零"
            });
        }

        // 在更新条件中限定普通用户，确保管理员无法被停用
        int affectedRows = await _db.Users
            .Where(user =>
                user.Id == id
                && user.Role == UserRoles.User)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.IsActive,
                    false),
                cancellationToken);

        if (affectedRows > 0)
        {
            return NoContent();
        }

        // 区分用户不存在、角色不允许和已经停用
        string? targetRole = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => user.Role)
            .SingleOrDefaultAsync(cancellationToken);

        if (targetRole is null)
        {
            // 创建用户不存在的响应
            return NotFound(new
            {
                message = "该用户不存在"
            });
        }

        if (targetRole != UserRoles.User)
        {
            // 创建管理员不能被停用的响应
            return Conflict(new
            {
                message = "只能停用普通用户，管理员不能被停用"
            });
        }

        // 普通用户已经处于停用状态时，重复操作也视为成功
        return NoContent();
    }
}