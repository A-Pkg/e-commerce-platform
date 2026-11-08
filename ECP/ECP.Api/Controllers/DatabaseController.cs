using ECP.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Controllers;

[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseController> _logger;

    public DatabaseController(AppDbContext db,ILogger<DatabaseController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("test")]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        try
        {
            // 实际尝试打开数据库连接
            await _db.Database.OpenConnectionAsync(cancellationToken);

            await _db.Database.CloseConnectionAsync();

            return Ok(new
            {
                success = true,
                message = "MySQL 数据库连接成功！"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL 数据库连接测试失败");

            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "数据库连接失败",
                detail: "请查看后端日志中的具体错误。");
        }
    }
}