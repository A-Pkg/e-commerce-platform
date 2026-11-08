using ECP.Api.Data;
using ECP.Api.Dtos.Users;
using ECP.Api.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Users;

/// <summary>
/// 当前登录用户的收货地址接口
/// </summary>
[ApiController]
[Route("api/user/addresses")]
[Authorize]
public class UserAddressesController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public UserAddressesController(AppDbContext db)
    {
        _db = db;
    }


    // 查询当前登录用户的收货地址，新地址排在前面
    [HttpGet]
    [ProducesResponseType(typeof(List<UserAddressResponse>),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        // 从已验证的登录身份读取用户编号，不接收客户端 UserId
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

        // 确认当前登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 只查询本人地址，按编号倒序，并创建地址响应
        List<UserAddressResponse> addresses = await _db.UserAddresses
            .AsNoTracking()
            .Where(address => address.UserId == userId)
            .OrderByDescending(address => address.Id)
            .Select(address => new UserAddressResponse
            {
                Id = address.Id,
                RecipientName = address.RecipientName,
                Phone = address.Phone,
                Address = address.Address,
                CreatedAtUtc = address.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // 地址保存的是 UTC 时间，读取后明确标记，确保响应携带 UTC 标识
        foreach (UserAddressResponse address in addresses)
        {
            address.CreatedAtUtc = DateTime.SpecifyKind(
                address.CreatedAtUtc,
                DateTimeKind.Utc);
        }

        // 没有地址时正常返回 HTTP 200 和空数组
        return Ok(addresses);
    }



    // 物理删除当前登录用户的收货地址
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份读取用户编号
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

        // 确认当前登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 地址编号必须为正整数
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "地址编号必须大于 0"
            });
        }

        // 同时限定地址编号和所属用户，直接删除本人记录
        int affectedRows = await _db.UserAddresses
            .Where(address => address.Id == id
                && address.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // 不存在和属于其他用户的地址统一返回 404
        if (affectedRows == 0)
        {
            return NotFound(new
            {
                message = "收货地址不存在"
            });
        }

        return NoContent();
    }



    // 修改当前登录用户的收货地址
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateUserAddressRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份读取用户编号
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

        // 确认当前登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 地址编号必须为正整数
        if (id <= 0)
        {
            return BadRequest(new
            {
                message = "地址编号必须大于 0"
            });
        }

        // 同时限定地址编号和所属用户，只读取本人地址
        // 此处需要修改实体，因此保留 EF Core 跟踪
        UserAddress? address = await _db.UserAddresses
            .SingleOrDefaultAsync(
                address => address.Id == id
                    && address.UserId == userId,
                cancellationToken);

        if (address is null)
        {
            return NotFound(new
            {
                message = "收货地址不存在"
            });
        }

        // 更新地址内容，保留原编号、所属用户和创建时间
        address.RecipientName = request.RecipientName.Trim();
        address.Phone = request.Phone.Trim();
        address.Address = request.Address.Trim();

        // 保存修改，成功后返回无正文的 HTTP 204
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }



    // 为当前登录用户新增收货地址
    [HttpPost]
    [ProducesResponseType(typeof(UserAddressResponse),StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserAddressRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份读取用户编号，不接收客户端 UserId
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

        // 确认当前登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 创建本人地址，去掉输入内容的首尾空白
        UserAddress address = new UserAddress
        {
            UserId = userId,
            RecipientName = request.RecipientName.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        // 保存地址，数据库自动生成地址编号
        _db.UserAddresses.Add(address);
        await _db.SaveChangesAsync(cancellationToken);

        // 创建响应，只返回地址展示所需的信息
        UserAddressResponse response = new UserAddressResponse
        {
            Id = address.Id,
            RecipientName = address.RecipientName,
            Phone = address.Phone,
            Address = address.Address,
            CreatedAtUtc = address.CreatedAtUtc
        };

        return StatusCode(
            StatusCodes.Status201Created,
            response);
    }
}