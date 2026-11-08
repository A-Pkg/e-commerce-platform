using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Controllers.Products;

/// <summary>
/// 商城端商品查询接口
/// </summary>
[ApiController]
[Route("api/products")]
[AllowAnonymous]
public class ProductsController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    // 查询已上架商品，允许未登录访问
    [HttpGet]
    [ProducesResponseType(typeof(List<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        // 只读查询已上架商品，关联分类并创建响应对象
        List<ProductResponse> products = await _db.Products
            .AsNoTracking()
            .Where(product => product.IsOnSale)
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
                })
            .OrderByDescending(product => product.Id)
            .ToListAsync(cancellationToken);

        // 没有已上架商品时，返回 HTTP 200 和空数组
        return Ok(products);
    }


    // 查询已上架商品的启用 SKU，允许游客访问
    [HttpGet("{productId:int}/skus")]
    [ProducesResponseType(typeof(List<ProductSkuResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSkus(
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

        // 商城端只允许查询已上架商品
        bool productAvailable = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == productId && product.IsOnSale,
                cancellationToken);

        if (!productAvailable)
        {
            // 商品不存在或已下架时，不返回它的 SKU
            return NotFound(new
            {
                message = "商品不存在或已下架"
            });
        }

        // 查询启用 SKU，零库存仍返回，供前端展示售罄状态
        List<ProductSkuResponse> skus = await _db.ProductSkus
            .AsNoTracking()
            .Where(sku => sku.ProductId == productId && sku.IsActive)
            .OrderBy(sku => sku.Id)
            .Select(sku => new ProductSkuResponse
            {
                Id = sku.Id,
                ProductId = sku.ProductId,
                SkuCode = sku.SkuCode,
                SpecificationText = string.Empty,
                Price = sku.Price,
                Stock = sku.Stock,
                IsActive = sku.IsActive
            })
            .ToListAsync(cancellationToken);

        // 商品没有启用 SKU 时，返回空数组
        if (skus.Count == 0)
        {
            return Ok(skus);
        }

        // 关联规格及规格值，读取启用 SKU 的规格展示内容
        List<SkuSpecificationDisplayRow> specificationRows = await (
            from relation in _db.ProductSkuSpecificationValues.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on relation.SkuId equals sku.Id
            join specification in _db.ProductSpecifications.AsNoTracking()
                on relation.SpecificationId equals specification.Id
            join specificationValue in _db.ProductSpecificationValues.AsNoTracking()
                on relation.SpecificationValueId equals specificationValue.Id
            where sku.ProductId == productId
                && sku.IsActive
                && specification.ProductId == productId
                && specificationValue.SpecificationId == relation.SpecificationId
            orderby sku.Id, specification.SortOrder, specification.Id
            // 创建规格展示中间数据
            select new SkuSpecificationDisplayRow
            {
                SkuId = sku.Id,
                SpecificationName = specification.Name,
                SpecificationValue = specificationValue.Value
            })
            .ToListAsync(cancellationToken);

        // 按 SKU 分组，避免逐个查询规格
        ILookup<int, SkuSpecificationDisplayRow> specificationLookup =
            specificationRows.ToLookup(row => row.SkuId);

        // 拼接规格文字，例如“颜色：黑色 / 存储：128GB”
        foreach (ProductSkuResponse sku in skus)
        {
            sku.SpecificationText = string.Join(
                " / ",
                specificationLookup[sku.Id].Select(
                    row => $"{row.SpecificationName}：{row.SpecificationValue}"));
        }

        return Ok(skus);
    }

    /// <summary>
    /// SKU 规格展示中间数据，仅用于组装响应
    /// </summary>
    private sealed class SkuSpecificationDisplayRow
    {
        // 所属 SKU 编号
        public int SkuId { get; set; }

        // 规格名称
        public string SpecificationName { get; set; } = string.Empty;

        // 规格值内容
        public string SpecificationValue { get; set; } = string.Empty;
    }
}