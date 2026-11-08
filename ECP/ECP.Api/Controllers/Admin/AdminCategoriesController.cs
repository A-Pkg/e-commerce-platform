using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using ECP.Api.Models.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员商品分类管理接口
/// </summary>
[ApiController]
[Route("api/admin/categories")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminCategoriesController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public AdminCategoriesController(AppDbContext db)
    {
        _db = db;
    }

    // 创建分类，分类名称不允许重复
    [HttpPost]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // 去除分类名称首尾空格
        string categoryName = request.Name.Trim();

        // 指定父分类时，检查它是否存在，以及是否为大分类
        if (request.ParentId.HasValue)
        {
            // 读取本次指定的父分类编号
            int parentId = request.ParentId.Value;

            // 只读查询父分类，不跟踪实体变化
            Category? parentCategory = await _db.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    category => category.Id == parentId,
                    cancellationToken);

            // 父分类不存在时，拒绝创建
            if (parentCategory is null)
            {
                // 返回父分类无效的提示
                return BadRequest(new
                {
                    message = "指定的父分类不存在"
                });
            }

            // 父分类必须是大分类，避免创建第三级分类
            if (parentCategory.ParentId.HasValue)
            {
                // 返回分类层级不符合要求的提示
                return BadRequest(new
                {
                    message = "只能选择大分类作为父分类，分类最多支持两级"
                });
            }
        }

        // 按数据库的比较规则检查是否已经存在同名分类
        bool nameExists = await _db.Categories.AnyAsync(
            category => category.Name == categoryName,
            cancellationToken);

        if (nameExists)
        {
            // 返回分类名称重复的提示
            return Conflict(new { message = "该分类名称已存在" });
        }

        // 创建分类实体，保存名称、父分类、排序和启用状态
        Category newCategory = new Category
        {
            ParentId = request.ParentId,
            Name = categoryName,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Categories.Add(newCategory);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 并发创建同名分类时，由数据库唯一索引阻止重复
            return Conflict(new { message = "该分类名称已存在" });
        }

        // 创建分类响应对象，不直接返回数据库实体
        CategoryResponse response = new CategoryResponse
        {
            Id = newCategory.Id,
            ParentId = newCategory.ParentId,
            Name = newCategory.Name,
            SortOrder = newCategory.SortOrder,
            IsActive = newCategory.IsActive,
            CreatedAtUtc = newCategory.CreatedAtUtc
        };

        // 返回 HTTP 201 和创建后的分类信息
        return StatusCode(StatusCodes.Status201Created, response);
    }

    // 查询全部分类，包括已启用和已停用的分类
    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        // 按排序值和编号升序查询，并为每条分类创建响应对象
        List<CategoryResponse> categories = await _db.Categories
            .AsNoTracking()
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

        // 没有分类时返回 HTTP 200 和空数组
        return Ok(categories);
    }

    // 修改指定分类，保留原有编号和创建时间
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        [FromRoute] int id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // 分类编号必须为正整数
        if (id <= 0)
        {
            return BadRequest(new { message = "分类编号必须大于 0" });
        }

        // 查询需要修改的分类，保留跟踪以便保存修改
        Category? category = await _db.Categories.SingleOrDefaultAsync(
            existingCategory => existingCategory.Id == id,
            cancellationToken);

        if (category is null)
        {
            return NotFound(new { message = "该分类不存在" });
        }

        // 指定父分类时，检查自身引用、父分类和现有子分类
        if (request.ParentId.HasValue)
        {
            // 读取本次指定的父分类编号
            int parentId = request.ParentId.Value;

            // 禁止将当前分类设为自己的父分类
            if (parentId == id)
            {
                // 返回自身引用无效的提示
                return BadRequest(new
                {
                    message = "不能将分类自身设为父分类"
                });
            }

            // 只读查询指定的父分类
            Category? parentCategory = await _db.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    existingCategory => existingCategory.Id == parentId,
                    cancellationToken);

            // 父分类必须存在
            if (parentCategory is null)
            {
                // 返回父分类不存在的提示
                return BadRequest(new
                {
                    message = "指定的父分类不存在"
                });
            }

            // 父分类必须是大分类，避免产生第三级分类
            if (parentCategory.ParentId.HasValue)
            {
                // 返回父分类层级不符合要求的提示
                return BadRequest(new
                {
                    message = "只能选择大分类作为父分类，分类最多支持两级"
                });
            }

            // 检查当前分类是否已有子分类，包含已停用的子分类
            bool hasChildren = await _db.Categories.AnyAsync(
                existingCategory => existingCategory.ParentId == id,
                cancellationToken);

            // 已有子分类时，当前分类必须继续作为大分类
            if (hasChildren)
            {
                // 返回不能将已有子分类的大分类降为子分类的提示
                return BadRequest(new
                {
                    message = "当前分类已有子分类，不能将其设为子分类"
                });
            }
        }

        // 去除分类名称首尾空格
        string categoryName = request.Name.Trim();

        // 排除当前分类，只检查其他分类是否使用了这个名称
        bool nameExists = await _db.Categories.AnyAsync(
            existingCategory => existingCategory.Id != id
                && existingCategory.Name == categoryName,
            cancellationToken);

        if (nameExists)
        {
            return Conflict(new { message = "该分类名称已存在" });
        }

        // 更新允许修改的字段，父分类编号为空时设为大分类
        category.ParentId = request.ParentId;
        category.Name = categoryName;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 并发修改为同名分类时，由数据库唯一索引阻止重复
            return Conflict(new { message = "该分类名称已存在" });
        }

        // 创建修改后的分类响应对象
        CategoryResponse response = new CategoryResponse
        {
            Id = category.Id,
            ParentId = category.ParentId,
            Name = category.Name,
            SortOrder = category.SortOrder,
            IsActive = category.IsActive,
            CreatedAtUtc = category.CreatedAtUtc
        };

        // 返回 HTTP 200 和更新后的分类信息
        return Ok(response);
    }
}