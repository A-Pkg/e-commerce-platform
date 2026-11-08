using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Common;
using ECP.Api.Dtos.Products;
using ECP.Api.Models.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员商品管理接口
/// </summary>
[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminProductsController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public AdminProductsController(AppDbContext db)
    {
        _db = db;
    }

    // 新增商品，创建时默认下架
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        // 只读查询所选分类，确认分类存在并获取分类名称
        Category? category = await _db.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existingCategory => existingCategory.Id == request.CategoryId,
                cancellationToken);

        if (category is null)
        {
            // 返回分类不存在的提示
            return BadRequest(new
            {
                message = "所选商品分类不存在"
            });
        }

        // 创建商品实体，价格和库存由后续 SKU 业务保存
        Product newProduct = new Product
        {
            CategoryId = category.Id,
            Name = request.Name.Trim(),
            Description = request.Description,
            IsOnSale = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        // 保存商品，由数据库生成商品编号
        _db.Products.Add(newProduct);
        await _db.SaveChangesAsync(cancellationToken);

        // 创建响应对象，返回实际保存的商品信息及分类名称
        ProductResponse response = new ProductResponse
        {
            Id = newProduct.Id,
            CategoryId = newProduct.CategoryId,
            CategoryName = category.Name,
            Name = newProduct.Name,
            Description = newProduct.Description,
            IsOnSale = newProduct.IsOnSale,
            CreatedAtUtc = newProduct.CreatedAtUtc
        };

        // 返回 HTTP 201，表示商品创建成功
        return StatusCode(StatusCodes.Status201Created, response);
    }



    // 修改商品公共信息，保留原有上下架状态和创建时间
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        [FromRoute] int id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        // 商品编号必须为正整数
        if (id <= 0)
        {
            // 返回商品编号无效的提示
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 查询待修改商品，保留跟踪以便保存字段变化
        Product? product = await _db.Products.SingleOrDefaultAsync(
            existingProduct => existingProduct.Id == id,
            cancellationToken);

        if (product is null)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 只读查询所选分类，确认分类存在并获取分类名称
        Category? category = await _db.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(
                existingCategory => existingCategory.Id == request.CategoryId,
                cancellationToken);

        if (category is null)
        {
            // 返回分类不存在的提示
            return BadRequest(new
            {
                message = "所选商品分类不存在"
            });
        }

        // 更新商品公共信息，其他字段保持原值
        product.CategoryId = category.Id;
        product.Name = request.Name.Trim();
        product.Description = request.Description;

        // 保存商品修改
        await _db.SaveChangesAsync(cancellationToken);

        // 创建响应对象，返回保存后的商品信息及分类名称
        ProductResponse response = new ProductResponse
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            CategoryName = category.Name,
            Name = product.Name,
            Description = product.Description,
            IsOnSale = product.IsOnSale,
            CreatedAtUtc = product.CreatedAtUtc
        };

        // 返回 HTTP 200，表示商品修改成功
        return Ok(response);
    }


    // 设置商品上下架状态，上架至少需要一个启用的 SKU
    [HttpPut("{id:int}/sale-status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSaleStatus(
        [FromRoute] int id,
        [FromBody] UpdateProductSaleStatusRequest request,
        CancellationToken cancellationToken)
    {
        // 商品编号必须为正整数
        if (id <= 0)
        {
            // 返回商品编号无效的提示
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 必须明确指定上架或下架
        if (!request.IsOnSale.HasValue)
        {
            // 返回状态缺失的提示
            return BadRequest(new
            {
                message = "请明确指定商品是否上架"
            });
        }

        // 查询商品，保留跟踪以便保存状态变化
        Product? product = await _db.Products.SingleOrDefaultAsync(
            existingProduct => existingProduct.Id == id,
            cancellationToken);

        if (product is null)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 上架至少需要一个启用 SKU，不额外限制图片、分类状态或库存
        if (request.IsOnSale.Value)
        {
            bool hasActiveSku = await _db.ProductSkus
                .AsNoTracking()
                .AnyAsync(
                    sku => sku.ProductId == id && sku.IsActive,
                    cancellationToken);

            if (!hasActiveSku)
            {
                // 没有启用的销售版本时，拒绝上架
                return Conflict(new
                {
                    message = "商品至少需要一个启用的 SKU 才能上架"
                });
            }
        }

        // 只修改上下架状态，保留其他商品信息及关联数据
        product.IsOnSale = request.IsOnSale.Value;

        try
        {
            // 保存状态，相同状态重复提交也视为成功
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 商品在查询后被删除时，提示重新加载
            return NotFound(new
            {
                message = "该商品已不存在，请重新加载列表"
            });
        }

        // 返回成功状态，不携带正文
        return NoContent();
    }


    // 按名称、分类和上下架状态分页查询商品
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PagedResponse<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? name,
        [FromQuery] int? categoryId,
        [FromQuery] string? status,
        [FromQuery] int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        // 当前阶段每页固定返回最多 20 条商品
        const int pageSize = 5;

        if (pageNumber <= 0)
        {
            // 创建页码无效的响应
            return BadRequest(new { message = "页码必须大于零" });
        }

        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            // 创建分类编号无效的响应
            return BadRequest(new { message = "分类编号必须大于零" });
        }

        string searchName = name?.Trim() ?? string.Empty;
        string selectedStatus = status?.Trim() ?? string.Empty;

        if (selectedStatus.Length > 0
            && selectedStatus != "on"
            && selectedStatus != "off")
        {
            // 创建上下架筛选条件无效的响应
            return BadRequest(new { message = "商品状态无效" });
        }

        // 只读关联商品与分类，创建列表响应查询
        IQueryable<ProductResponse> query = _db.Products
            .AsNoTracking()
            .Join(
                _db.Categories.AsNoTracking(),
                product => product.CategoryId,
                category => category.Id,
                (product, category) => new ProductResponse
                {
                    Id = product.Id,
                    CategoryId = product.CategoryId,
                    CategoryName = category.Name,
                    Name = product.Name,
                    Description = product.Description,
                    IsOnSale = product.IsOnSale,
                    CreatedAtUtc = product.CreatedAtUtc
                });

        // 名称按包含关系查询，不只筛选当前页
        if (searchName.Length > 0)
        {
            query = query.Where(product => product.Name.Contains(searchName));
        }

        // 沿用现有两级分类规则，大分类同时包含其直接子分类
        if (categoryId.HasValue)
        {
            int selectedCategoryId = categoryId.Value;
            query = query.Where(product =>
                product.CategoryId == selectedCategoryId
                || _db.Categories.Any(category =>
                    category.Id == product.CategoryId
                    && category.ParentId == selectedCategoryId));
        }

        if (selectedStatus.Length > 0)
        {
            bool isOnSale = selectedStatus == "on";
            query = query.Where(product => product.IsOnSale == isOnSale);
        }

        // 先统计全部匹配商品，再计算有效页码
        int totalCount = await query.CountAsync(cancellationToken);
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize));
        int currentPageNumber = Math.Min(pageNumber, totalPages);
        int skipCount = (currentPageNumber - 1) * pageSize;

        // 沿用商品编号倒序，只读取当前页商品
        List<ProductResponse> products = await query
            .OrderByDescending(product => product.Id)
            .Skip(skipCount)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // 数据库保存 UTC 时间，读取后补充 UTC 标记
        foreach (ProductResponse product in products)
        {
            product.CreatedAtUtc = DateTime.SpecifyKind(
                product.CreatedAtUtc,
                DateTimeKind.Utc);
        }

        // 创建共用分页响应，空结果保留第 1 页和空列表
        PagedResponse<ProductResponse> response = new PagedResponse<ProductResponse>
        {
            PageNumber = currentPageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = products
        };

        return Ok(response);
    }
}
