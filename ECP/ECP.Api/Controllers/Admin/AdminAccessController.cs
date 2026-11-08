using ECP.Api.Constants.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员访问权限验证接口
/// </summary>
[ApiController]
[Route("api/admin/access")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminAccessController : ControllerBase
{
    // 检查当前用户是否拥有管理员访问权限
    [HttpGet("check")]
    public IActionResult CheckAccess()
    {
        // 返回管理员权限验证通过的结果
        return Ok(new
        {
            message = "管理员权限验证通过"
        });
    }
}