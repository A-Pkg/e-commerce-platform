using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Controllers.Products;

/// <summary>
/// 商城端商品分类查询接口
/// </summary>
[ApiController]
[Route("api/categories")]
[AllowAnonymous]
public class CategoriesController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public CategoriesController(AppDbContext db)
    {
        _db = db;
    }

    // 查询已启用的分类，允许未登录访问
    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        // 筛选已启用的分类，按排序值和编号排序，并创建响应对象
        List<CategoryResponse> categories = await _db.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Id)
            .Select(category => new CategoryResponse
            {
                Id = category.Id,
                ParentId = category.ParentId,
                Name = category.Name,
                SortOrder = category.SortOrder,
                IsActive = category.IsActive,
                CreatedAtUtc = category.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // 没有已启用的分类时返回 HTTP 200 和空数组
        return Ok(categories);
    }
}