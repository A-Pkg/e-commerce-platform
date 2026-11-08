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
/// 管理员商品规格与规格值接口
/// </summary>
[ApiController]
[Route("api/admin/products/{productId:int}/specifications")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminProductSpecificationsController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public AdminProductSpecificationsController(AppDbContext db)
    {
        _db = db;
    }

    // 查询指定商品的规格及其规格值
    [HttpGet]
    [ProducesResponseType(typeof(List<ProductSpecificationResponse>),StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromRoute] int productId,
        CancellationToken cancellationToken)
    {
        // 商品编号必须为正整数
        if (productId <= 0)
        {
            // 返回商品编号无效的提示
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 检查商品是否存在
        bool productExists = await ProductExistsAsync(
            productId,
            cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 查询商品规格，按排序值和编号排列，并创建响应对象
        List<ProductSpecificationResponse> specifications =
            await _db.ProductSpecifications
                .AsNoTracking()
                .Where(specification => specification.ProductId == productId)
                .OrderBy(specification => specification.SortOrder)
                .ThenBy(specification => specification.Id)
                .Select(specification => new ProductSpecificationResponse
                {
                    Id = specification.Id,
                    ProductId = specification.ProductId,
                    Name = specification.Name,
                    SortOrder = specification.SortOrder,
                    CreatedAtUtc = specification.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

        // 商品尚无规格时，返回空数组
        if (specifications.Count == 0)
        {
            return Ok(specifications);
        }

        // 关联规格查询当前商品的全部规格值，创建响应对象
        List<ProductSpecificationValueResponse> values = await (
            from specificationValue in
                _db.ProductSpecificationValues.AsNoTracking()
            join specification in _db.ProductSpecifications.AsNoTracking()
                on specificationValue.SpecificationId equals specification.Id
            where specification.ProductId == productId
            orderby specificationValue.SortOrder, specificationValue.Id
            select new ProductSpecificationValueResponse
            {
                Id = specificationValue.Id,
                SpecificationId = specificationValue.SpecificationId,
                Value = specificationValue.Value,
                SortOrder = specificationValue.SortOrder,
                CreatedAtUtc = specificationValue.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // 按所属规格分组，避免逐个规格查询数据库
        ILookup<int, ProductSpecificationValueResponse> valueLookup =
            values.ToLookup(value => value.SpecificationId);

        // 将规格值放入对应规格，没有规格值时保留空列表
        foreach (ProductSpecificationResponse specification in specifications)
        {
            specification.Values = valueLookup[specification.Id].ToList();
        }

        return Ok(specifications);
    }

    // 为指定商品新增规格
    [HttpPost]
    [ProducesResponseType(typeof(ProductSpecificationResponse),StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromRoute] int productId,
        [FromBody] CreateProductSpecificationRequest request,
        CancellationToken cancellationToken)
    {
        // 商品编号必须为正整数
        if (productId <= 0)
        {
            // 返回商品编号无效的提示
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 检查商品是否存在
        bool productExists = await ProductExistsAsync(
            productId,
            cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 去除规格名称首尾空格
        string specificationName = request.Name.Trim();

        // 同一个商品不允许存在同名规格
        bool nameExists = await _db.ProductSpecifications.AnyAsync(
            specification => specification.ProductId == productId
                && specification.Name == specificationName,
            cancellationToken);

        if (nameExists)
        {
            // 返回规格名称重复的提示
            return Conflict(new
            {
                message = "该商品已存在同名规格"
            });
        }

        // 创建规格实体，所属商品取自接口路径
        ProductSpecification newSpecification = new ProductSpecification
        {
            ProductId = productId,
            Name = specificationName,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProductSpecifications.Add(newSpecification);

        try
        {
            // 保存规格，由数据库生成编号
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 并发创建同名规格时，由数据库唯一索引阻止重复
            return Conflict(new
            {
                message = "该商品已存在同名规格"
            });
        }

        // 创建响应对象，新规格的规格值列表默认为空
        ProductSpecificationResponse response =
            new ProductSpecificationResponse
            {
                Id = newSpecification.Id,
                ProductId = newSpecification.ProductId,
                Name = newSpecification.Name,
                SortOrder = newSpecification.SortOrder,
                CreatedAtUtc = newSpecification.CreatedAtUtc
            };

        return StatusCode(StatusCodes.Status201Created, response);
    }


    // 为当前商品下的指定规格新增规格值
    [HttpPost("{specificationId:int}/values")]
    [ProducesResponseType(typeof(ProductSpecificationValueResponse),StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateValue(
        [FromRoute] int productId,
        [FromRoute] int specificationId,
        [FromBody] CreateProductSpecificationValueRequest request,
        CancellationToken cancellationToken)
    {
        // 商品编号必须为正整数
        if (productId <= 0)
        {
            // 返回商品编号无效的提示
            return BadRequest(new
            {
                message = "商品编号必须大于 0"
            });
        }

        // 规格编号必须为正整数
        if (specificationId <= 0)
        {
            // 返回规格编号无效的提示
            return BadRequest(new
            {
                message = "规格编号必须大于 0"
            });
        }

        // 检查商品是否存在
        bool productExists = await ProductExistsAsync(
            productId,
            cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 同时检查规格存在且属于当前商品，防止跨商品写入
        bool specificationExists = await _db.ProductSpecifications
            .AsNoTracking()
            .AnyAsync(
                specification => specification.Id == specificationId
                    && specification.ProductId == productId,
                cancellationToken);

        if (!specificationExists)
        {
            // 返回当前商品下规格不存在的提示
            return NotFound(new
            {
                message = "该商品下不存在指定规格"
            });
        }

        // 去除规格值首尾空格
        string specificationValue = request.Value.Trim();

        // 同一种规格下不允许存在相同的规格值
        bool valueExists = await _db.ProductSpecificationValues.AnyAsync(
            value => value.SpecificationId == specificationId
                && value.Value == specificationValue,
            cancellationToken);

        if (valueExists)
        {
            // 返回规格值重复的提示
            return Conflict(new
            {
                message = "该规格下已存在相同规格值"
            });
        }

        // 创建规格值实体，所属规格取自已验证的接口路径
        ProductSpecificationValue newValue = new ProductSpecificationValue
        {
            SpecificationId = specificationId,
            Value = specificationValue,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProductSpecificationValues.Add(newValue);

        try
        {
            // 保存规格值，由数据库生成编号
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 并发创建相同规格值时，由数据库唯一索引阻止重复
            return Conflict(new
            {
                message = "该规格下已存在相同规格值"
            });
        }

        // 创建响应对象，返回实际保存的规格值
        ProductSpecificationValueResponse response =
            new ProductSpecificationValueResponse
            {
                Id = newValue.Id,
                SpecificationId = newValue.SpecificationId,
                Value = newValue.Value,
                SortOrder = newValue.SortOrder,
                CreatedAtUtc = newValue.CreatedAtUtc
            };

        return StatusCode(StatusCodes.Status201Created, response);
    }

        // 删除当前商品下已经清空规格值的规格
    [HttpDelete("{specificationId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        [FromRoute] int productId,
        [FromRoute] int specificationId,
        CancellationToken cancellationToken)
    {
        // 商品编号和规格编号必须为正整数
        if (productId <= 0 || specificationId <= 0)
        {
            // 返回编号无效的提示
            return BadRequest(new
            {
                message = "商品编号和规格编号必须大于 0"
            });
        }

        // 检查商品是否存在
        bool productExists = await ProductExistsAsync(
            productId,
            cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 查询待删除规格，同时检查它属于当前商品
        ProductSpecification? specification =
            await _db.ProductSpecifications.SingleOrDefaultAsync(
                item => item.Id == specificationId
                    && item.ProductId == productId,
                cancellationToken);

        if (specification is null)
        {
            // 返回当前商品下规格不存在的提示
            return NotFound(new
            {
                message = "该商品下不存在指定规格"
            });
        }

        // 规格下还有值时，要求先逐个删除规格值
        bool hasValues = await _db.ProductSpecificationValues
            .AsNoTracking()
            .AnyAsync(
                value => value.SpecificationId == specificationId,
                cancellationToken);

        if (hasValues)
        {
            // 返回需要先清空规格值的提示
            return Conflict(new
            {
                message = "该规格下还有规格值，请先删除规格值"
            });
        }

        // 标记删除当前规格，不删除其他关联数据
        _db.ProductSpecifications.Remove(specification);

        try
        {
            // 保存规格删除
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 当前规格已被其他请求删除
            return NotFound(new
            {
                message = "该规格已不存在，请刷新后重试"
            });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1451)
        {
            // 检查之后若又新增规格值，由数据库外键阻止删除
            return Conflict(new
            {
                message = "该规格仍有关联数据，请刷新并先清空规格值"
            });
        }

        // 删除成功，无需返回正文
        return NoContent();
    }

    // 删除当前商品指定规格下未被 SKU 使用的规格值
    [HttpDelete("{specificationId:int}/values/{valueId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteValue(
        [FromRoute] int productId,
        [FromRoute] int specificationId,
        [FromRoute] int valueId,
        CancellationToken cancellationToken)
    {
        // 商品、规格和规格值编号必须为正整数
        if (productId <= 0 || specificationId <= 0 || valueId <= 0)
        {
            // 返回编号无效的提示
            return BadRequest(new
            {
                message = "商品编号、规格编号和规格值编号必须大于 0"
            });
        }

        // 检查商品是否存在
        bool productExists = await ProductExistsAsync(
            productId,
            cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 检查指定规格是否属于当前商品
        bool specificationExists = await _db.ProductSpecifications
            .AsNoTracking()
            .AnyAsync(
                specification => specification.Id == specificationId
                    && specification.ProductId == productId,
                cancellationToken);

        if (!specificationExists)
        {
            // 返回当前商品下规格不存在的提示
            return NotFound(new
            {
                message = "该商品下不存在指定规格"
            });
        }

        // 查询待删除规格值，同时检查它属于当前规格
        ProductSpecificationValue? specificationValue =
            await _db.ProductSpecificationValues.SingleOrDefaultAsync(
                value => value.Id == valueId
                    && value.SpecificationId == specificationId,
                cancellationToken);

        if (specificationValue is null)
        {
            // 返回当前规格下规格值不存在的提示
            return NotFound(new
            {
                message = "该规格下不存在指定规格值"
            });
        }

        // 检查全部 SKU 关联，停用 SKU 使用的规格值也不能删除
        bool isUsedBySku = await _db.ProductSkuSpecificationValues
            .AsNoTracking()
            .AnyAsync(
                relation => relation.SpecificationValueId == valueId
                    && relation.SpecificationId == specificationId,
                cancellationToken);

        if (isUsedBySku)
        {
            // 返回规格值正在使用的提示
            return Conflict(new
            {
                message = "该规格值已被 SKU 使用，不能删除"
            });
        }

        // 标记删除当前规格值
        _db.ProductSpecificationValues.Remove(specificationValue);

        try
        {
            // 保存规格值删除
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 当前规格值已被其他请求删除
            return NotFound(new
            {
                message = "该规格值已不存在，请刷新后重试"
            });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1451)
        {
            // 检查之后若又被 SKU 引用，由数据库外键阻止删除
            return Conflict(new
            {
                message = "该规格值已有关联数据，不能删除，请刷新后重试"
            });
        }

        // 删除成功，无需返回正文
        return NoContent();
    }

    // 只读检查指定商品是否存在
    private async Task<bool> ProductExistsAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        return await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == productId,
                cancellationToken);
    }
}