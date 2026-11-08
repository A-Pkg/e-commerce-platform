using ECP.Api.Constants.Stocks;
using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Products;
using ECP.Api.Models.Products;
using ECP.Api.Models.Stocks;
using ECP.Api.Services.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员商品 SKU 查询、创建与状态管理接口
/// </summary>
[ApiController]
[Route("api/admin/products/{productId:int}/skus")]
[Authorize(Roles = UserRoles.Admin)]
public class AdminProductSkusController : ControllerBase
{
    // 数据库上下文，用于现有 SKU 查询
    private readonly AppDbContext _db;

    // SKU 创建服务
    private readonly ProductSkuCreationService _skuCreationService;

    // 初始化数据库上下文和 SKU 创建服务
    public AdminProductSkusController(AppDbContext db,ProductSkuCreationService skuCreationService)
    {
        _db = db;
        _skuCreationService = skuCreationService;
    }


    // 为指定商品创建 SKU
    [HttpPost]
    [ProducesResponseType(typeof(ProductSkuResponse),StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(
        [FromRoute] int productId,
        [FromBody] CreateProductSkuRequest request,
        CancellationToken cancellationToken)
    {
        // 从已经验证的登录身份中读取操作用户编号
        string? userIdValue =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        // 用户编号必须为有效的正整数
        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int operatorUserId)
            || operatorUserId <= 0)
        {
            // 返回登录身份无效的提示
            return Unauthorized(new
            {
                message = "当前登录身份无效，请重新登录"
            });
        }

        // 调用创建服务，统一执行校验和事务保存
        ProductSkuCreationResult result =
            await _skuCreationService.CreateAsync(
                productId,
                request,
                operatorUserId,
                cancellationToken);

        if (result.Succeeded)
        {
            // 返回 HTTP 201 和实际创建的 SKU
            return StatusCode(
                StatusCodes.Status201Created,
                result.Sku);
        }

        // 将业务结果转换为对应的 HTTP 响应
        switch (result.Failure)
        {
            case ProductSkuCreationFailure.InvalidInput:
                // 返回输入无效的提示
                return BadRequest(new
                {
                    message = result.Message
                });

            case ProductSkuCreationFailure.ProductNotFound:
                // 返回商品不存在的提示
                return NotFound(new
                {
                    message = result.Message
                });

            case ProductSkuCreationFailure.OperatorNotFound:
                // 返回操作用户不存在的提示
                return Unauthorized(new
                {
                    message = result.Message
                });

            case ProductSkuCreationFailure.DuplicateCombination:
            case ProductSkuCreationFailure.RelatedDataChanged:
                // 返回重复组合或关联数据变化的提示
                return Conflict(new
                {
                    message = result.Message
                });

            case ProductSkuCreationFailure.SkuCodeConflict:
                // 编码重试仍未成功时，提示稍后重试
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        message = result.Message
                    });

            default:
                // 返回未预期结果的统一提示
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "创建 SKU 失败，请稍后重试"
                    });
        }
    }


    // 编辑 SKU 的售价、库存和启用状态，规格组合保持不变
    [HttpPut("{skuId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] int productId,
        [FromRoute] int skuId,
        [FromBody] UpdateProductSkuRequest request,
        CancellationToken cancellationToken)
    {
        // 商品和 SKU 编号必须为正整数
        if (productId <= 0 || skuId <= 0)
        {
            // 返回编号无效的提示
            return BadRequest(new
            {
                message = "商品编号和 SKU 编号必须大于 0"
            });
        }

        // 明确要求填写所有可编辑字段
        if (!request.Price.HasValue
            || !request.Stock.HasValue
            || !request.IsActive.HasValue
            || !request.ExpectedStock.HasValue)
        {
            // 返回参数缺失的提示
            return BadRequest(new
            {
                message = "请完整填写 SKU 编辑信息"
            });
        }

        decimal price = request.Price.Value;
        int stock = request.Stock.Value;
        bool isActive = request.IsActive.Value;
        int expectedStock = request.ExpectedStock.Value;

        // 售价最多保留两位小数，不依赖数据库自动舍入
        if (decimal.Round(price, 2) != price)
        {
            // 返回金额精度错误的提示
            return BadRequest(new
            {
                message = "售价最多保留两位小数"
            });
        }

        // 从已验证的登录身份中取得操作用户编号
        string? userIdValue =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!int.TryParse(
                userIdValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int operatorUserId)
            || operatorUserId <= 0)
        {
            // 返回登录身份无效的提示
            return Unauthorized(new
            {
                message = "当前登录身份无效，请重新登录"
            });
        }

        // 检查操作用户仍然存在
        bool operatorExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == operatorUserId,
                cancellationToken);

        if (!operatorExists)
        {
            // 返回操作用户不存在的提示
            return Unauthorized(new
            {
                message = "当前操作用户不存在，请重新登录"
            });
        }

        // 同一次库存调整重试时复用相同流水号
        string movementNo = FormattableString.Invariant(
            $"ADJ:{request.RequestId.ToLowerInvariant()}:{skuId}");

        try
        {
            // 库存更新和调整流水必须在同一事务中完成
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(cancellationToken);

            // 同时限定商品和 SKU 编号，防止跨商品操作
            ProductSku? sku = await _db.ProductSkus
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == skuId
                        && item.ProductId == productId,
                    cancellationToken);

            if (sku is null)
            {
                // 返回当前商品下未找到 SKU 的提示
                return NotFound(new
                {
                    message = "该商品下不存在指定的 SKU"
                });
            }

            // 已完成的库存调整不再重复执行
            bool alreadyProcessed = await _db.StockMovements
                .AsNoTracking()
                .AnyAsync(
                    movement => movement.MovementNo == movementNo,
                    cancellationToken);

            if (alreadyProcessed)
            {
                return NoContent();
            }

            // 编辑期间发生购买或其他调整时，要求重新读取库存
            if (sku.Stock != expectedStock)
            {
                // 返回库存已经变化的提示
                return Conflict(new
                {
                    message = "库存已发生变化，请重新打开编辑后再保存"
                });
            }

            // 所有字段均未变化时，直接返回成功，不产生流水
            if (sku.Price == price
                && sku.Stock == stock
                && sku.IsActive == isActive)
            {
                return NoContent();
            }

            // 更新时再次检查库存和读取到的其他字段，避免覆盖并发修改
            int affectedRows = await _db.ProductSkus
                .Where(item => item.Id == skuId
                    && item.ProductId == productId
                    && item.Stock == expectedStock
                    && item.Price == sku.Price
                    && item.IsActive == sku.IsActive)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Price, price)
                        .SetProperty(item => item.Stock, stock)
                        .SetProperty(item => item.IsActive, isActive),
                    cancellationToken);

            if (affectedRows == 0)
            {
                // 条件更新失败时，不覆盖其他请求的结果
                return Conflict(new
                {
                    message = "SKU 数据已发生变化，请重新打开编辑后再保存"
                });
            }

            // 库存允许范围内的两个非负 int 相减不会溢出
            int quantityChange = stock - expectedStock;

            if (quantityChange != 0)
            {
                // 创建后台库存调整流水，记录差量和调整后的余额
                StockMovement movement = new StockMovement
                {
                    MovementNo = movementNo,
                    SkuId = skuId,
                    QuantityChange = quantityChange,
                    BalanceAfter = stock,
                    Reason = StockMovementReasons.AdminAdjustment,
                    OrderId = null,
                    OperatorUserId = operatorUserId,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.StockMovements.Add(movement);
                await _db.SaveChangesAsync(cancellationToken);
            }

            // 更新和必要的流水保存成功后，统一提交
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1062)
        {
            // 并发重复保存时，事务会回滚，避免重复调整库存
            return Conflict(new
            {
                message = "本次保存请求已被处理，请刷新 SKU 列表"
            });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && mysqlException.Number == 1452)
        {
            // 操作用户或其他关联记录发生变化时，整体回滚
            return Conflict(new
            {
                message = "关联数据已发生变化，请刷新后重试"
            });
        }
    }


    // 设置指定商品下某个 SKU 的启用状态
    [HttpPut("{skuId:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] int productId,
        [FromRoute] int skuId,
        [FromBody] UpdateProductSkuStatusRequest request,
        CancellationToken cancellationToken)
    {
        // 商品编号和 SKU 编号都必须为正整数
        if (productId <= 0 || skuId <= 0)
        {
            // 返回编号无效的提示
            return BadRequest(new
            {
                message = "商品编号和 SKU 编号必须大于 0"
            });
        }

        // 明确要求传入 true 或 false，不能省略或传入 null
        if (!request.IsActive.HasValue)
        {
            // 返回状态缺失的提示
            return BadRequest(new
            {
                message = "请明确指定 SKU 是否启用"
            });
        }

        // 检查商品是否存在
        bool productExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == productId,
                cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 同时限定商品和 SKU 编号，禁止操作其他商品的 SKU
        ProductSku? sku = await _db.ProductSkus
            .SingleOrDefaultAsync(
                existingSku => existingSku.Id == skuId
                    && existingSku.ProductId == productId,
                cancellationToken);

        if (sku is null)
        {
            // 返回当前商品下未找到 SKU 的提示
            return NotFound(new
            {
                message = "该商品下不存在指定的 SKU"
            });
        }

        // 只修改启用状态，保留价格、库存和规格关联
        sku.IsActive = request.IsActive.Value;

        try
        {
            // 保存发生变化的状态字段，相同状态重复提交也视为成功
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // SKU 在查询后被其他请求删除时，提示重新加载
            return NotFound(new
            {
                message = "该 SKU 已不存在，请重新加载列表"
            });
        }

        // 返回成功状态，不携带正文
        return NoContent();
    }


    // 查询指定商品的全部 SKU，包含启用和停用项
    [HttpGet]
    [ProducesResponseType(typeof(List<ProductSkuResponse>),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

        // 检查商品是否存在，区分商品不存在和商品尚无 SKU
        bool productExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == productId,
                cancellationToken);

        if (!productExists)
        {
            // 返回商品不存在的提示
            return NotFound(new
            {
                message = "该商品不存在"
            });
        }

        // 查询商品全部 SKU，创建返回对象，规格文字稍后填充
        List<ProductSkuResponse> skus = await _db.ProductSkus
            .AsNoTracking()
            .Where(sku => sku.ProductId == productId)
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

        // 商品存在但没有 SKU 时，直接返回空数组
        if (skus.Count == 0)
        {
            return Ok(skus);
        }

        // 关联 SKU、规格和规格值，按规格顺序读取展示内容
        List<SkuSpecificationDisplayRow> specificationRows = await (
            from relation in _db.ProductSkuSpecificationValues.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on relation.SkuId equals sku.Id
            join specification in _db.ProductSpecifications.AsNoTracking()
                on relation.SpecificationId equals specification.Id
            join specificationValue in _db.ProductSpecificationValues.AsNoTracking()
                on relation.SpecificationValueId equals specificationValue.Id
            where sku.ProductId == productId
                && specification.ProductId == productId
                && specificationValue.SpecificationId == relation.SpecificationId
            orderby specification.SortOrder, specification.Id
            // 创建规格展示中间数据
            select new SkuSpecificationDisplayRow
            {
                SkuId = sku.Id,
                SpecificationName = specification.Name,
                SpecificationValue = specificationValue.Value
            })
            .ToListAsync(cancellationToken);

        // 按 SKU 编号建立分组，避免逐个 SKU 请求数据库
        ILookup<int, SkuSpecificationDisplayRow> specificationLookup =
            specificationRows.ToLookup(row => row.SkuId);

        // 拼接规格文字，例如“颜色：黑色 / 容量：256GB”
        foreach (ProductSkuResponse sku in skus)
        {
            sku.SpecificationText = string.Join(
                " / ",
                specificationLookup[sku.Id].Select(
                    row => $"{row.SpecificationName}：{row.SpecificationValue}"));
        }

        // 返回真实 SKU 列表
        return Ok(skus);
    }

    /// <summary>
    /// SKU 规格展示中间数据，仅供当前控制器组装返回内容
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