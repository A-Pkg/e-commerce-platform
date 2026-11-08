using ECP.Api.Data;
using ECP.Api.Dtos.Carts;
using ECP.Api.Models.Carts;
using ECP.Api.Models.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;
using System.Data;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Carts;

/// <summary>
/// 当前登录用户的购物车接口
/// </summary>
[ApiController]
[Route("api/cart/items")]
[Authorize]
public class CartItemsController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public CartItemsController(AppDbContext db)
    {
        _db = db;
    }


    // 查询当前登录用户的购物车，保留暂时不可购买的条目
    [HttpGet]
    [ProducesResponseType(typeof(List<CartItemResponse>),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetList(
        CancellationToken cancellationToken)
    {
        // 从已验证的身份中读取用户编号，不接收客户端 UserId
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 只查询本人条目，不过滤下架商品、停用或售罄 SKU
        List<CartItemResponse> items = await (
            from cartItem in _db.CartItems.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on cartItem.SkuId equals sku.Id
            join product in _db.Products.AsNoTracking()
                on sku.ProductId equals product.Id
            where cartItem.UserId == userId
            orderby cartItem.Id descending
            // 创建购物车响应，价格和库存取当前 SKU 数据
            select new CartItemResponse
            {
                Id = cartItem.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                SkuId = sku.Id,
                SkuCode = sku.SkuCode,
                SpecificationText = string.Empty,
                Price = sku.Price,
                Stock = sku.Stock,
                Quantity = cartItem.Quantity,
                IsSelected = cartItem.IsSelected,
                IsAvailable = product.IsOnSale
                    && sku.IsActive
                    && sku.Stock > 0
                    && sku.Stock >= cartItem.Quantity,
                UnavailableReason = !product.IsOnSale
                    ? "商品已下架"
                    : !sku.IsActive
                        ? "商品规格已停用"
                        : sku.Stock <= 0
                            ? "商品已售罄"
                            : sku.Stock < cartItem.Quantity
                                ? "库存不足，请调整购买数量"
                                : string.Empty
            })
            .ToListAsync(cancellationToken);

        // 空购物车正常返回 HTTP 200 和空数组
        if (items.Count == 0)
        {
            return Ok(items);
        }

        // 只读取本次本人购物车涉及的 SKU 规格
        int[] skuIds = items
            .Select(item => item.SkuId)
            .ToArray();

        // 一次查询全部规格文字，避免逐条查询购物车
        List<CartSkuSpecificationDisplayRow> specificationRows = await (
            from relation in
                _db.ProductSkuSpecificationValues.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on relation.SkuId equals sku.Id
            join specification in
                _db.ProductSpecifications.AsNoTracking()
                on relation.SpecificationId equals specification.Id
            join specificationValue in
                _db.ProductSpecificationValues.AsNoTracking()
                on relation.SpecificationValueId equals specificationValue.Id
            where skuIds.Contains(sku.Id)
                && specification.ProductId == sku.ProductId
                && specificationValue.SpecificationId
                    == relation.SpecificationId
            orderby sku.Id, specification.SortOrder, specification.Id
            // 创建用于拼接规格文字的中间数据
            select new CartSkuSpecificationDisplayRow
            {
                SkuId = sku.Id,
                SpecificationName = specification.Name,
                SpecificationValue = specificationValue.Value
            })
            .ToListAsync(cancellationToken);

        // 按 SKU 分组，供各条购物车记录读取
        ILookup<int, CartSkuSpecificationDisplayRow> specificationLookup =
            specificationRows.ToLookup(row => row.SkuId);

        foreach (CartItemResponse item in items)
        {
            // 未选择规格的默认 SKU 保持空字符串
            item.SpecificationText = string.Join(
                " / ",
                specificationLookup[item.SkuId].Select(
                    row => $"{row.SpecificationName}：{row.SpecificationValue}"));
        }

        return Ok(items);
    }

    /// <summary>
    /// 购物车 SKU 规格展示中间数据，仅用于组装查询结果
    /// </summary>
    private sealed class CartSkuSpecificationDisplayRow
    {
        // 所属 SKU 编号
        public int SkuId { get; set; }

        // 规格名称
        public string SpecificationName { get; set; } = string.Empty;

        // 规格值内容
        public string SpecificationValue { get; set; } = string.Empty;
    }


    // 批量设置本人购物车条目的勾选状态，任意条目无效时整批回滚
    [HttpPut("selection")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSelections(
        [FromBody] UpdateCartItemsSelectionRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中读取用户编号
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 检查列表、每个编号及明确提交的目标状态
        if (request.CartItemIds is null
            || request.CartItemIds.Count == 0
            || request.CartItemIds.Any(id => id <= 0)
            || !request.IsSelected.HasValue)
        {
            // 返回参数无效的提示
            return BadRequest(new
            {
                message = "请提交有效的购物车条目编号，并明确指定是否勾选"
            });
        }

        // 创建编号快照，重复编号只处理一次
        int[] cartItemIds = request.CartItemIds
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        // 保存本次要求的最终状态
        bool isSelected = request.IsSelected.Value;

        try
        {
            // 更新与核对必须属于同一事务，核对失败时撤销全部修改
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            // 只更新本人且在本次列表中的条目，不修改数量或库存
            await _db.CartItems
                .Where(item => item.UserId == userId
                    && cartItemIds.Contains(item.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        item => item.IsSelected,
                        isSelected),
                    cancellationToken);

            // 核对全部编号都属于本人，并且已经达到目标状态
            // 不依赖受影响行数，兼容原状态相同的重复提交
            int matchedCount = await _db.CartItems
                .AsNoTracking()
                .CountAsync(
                    item => item.UserId == userId
                        && cartItemIds.Contains(item.Id)
                        && item.IsSelected == isSelected,
                    cancellationToken);

            if (matchedCount != cartItemIds.Length)
            {
                // 任意编号无效时，撤销其他有效条目已经执行的修改
                await transaction.RollbackAsync(cancellationToken);

                // 不区分不存在、属于他人或关联条目发生变化
                return NotFound(new
                {
                    message = "购物车条目不存在或已发生变化，请刷新后重试"
                });
            }

            // 全部核对通过后才统一提交
            await transaction.CommitAsync(cancellationToken);

            return NoContent();
        }
        catch (MySqlException exception) when (
            exception.Number == 1213
            || exception.Number == 1205)
        {
            // 遇到死锁或锁等待超时，事务释放时回滚未提交修改
            return Conflict(new
            {
                message = "购物车正在被其他请求修改，请稍后重试"
            });
        }
    }


    // 设置本人购物车条目的勾选状态，相同状态重复提交也视为成功
    [HttpPut("{id:int}/selection")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSelection(
        int id,
        [FromBody] UpdateCartItemSelectionRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中读取用户编号
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 检查条目编号及明确提交的目标状态
        if (id <= 0 || !request.IsSelected.HasValue)
        {
            // 返回参数无效的提示
            return BadRequest(new
            {
                message = "购物车条目编号必须大于 0，并明确指定是否勾选"
            });
        }

        // 保存客户端要求的最终状态，不采用状态取反
        bool isSelected = request.IsSelected.Value;

        // 只修改本人条目的勾选状态，且仅更新状态不同的记录
        int affectedRows = await _db.CartItems
            .Where(item => item.Id == id
                && item.UserId == userId
                && item.IsSelected != isSelected)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.IsSelected,
                    isSelected),
                cancellationToken);

        if (affectedRows == 0)
        {
            // 未更新可能是状态已经相同，也可能是本人条目不存在
            bool itemExists = await _db.CartItems
                .AsNoTracking()
                .AnyAsync(
                    item => item.Id == id
                        && item.UserId == userId,
                    cancellationToken);

            if (!itemExists)
            {
                // 不区分条目不存在或属于其他用户
                return NotFound(new
                {
                    message = "购物车条目不存在"
                });
            }
        }

        // ExecuteUpdateAsync 已直接写入数据库，无需 SaveChangesAsync
        return NoContent();
    }



    // 删除本人购物车条目，商品状态和库存不影响删除
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id,CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中读取用户编号
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 同时限制条目编号和所属用户，不修改商品或库存
        int affectedRows = await _db.CartItems
            .Where(item => item.Id == id && item.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (affectedRows == 0)
        {
            // 不区分不存在、属于他人或已经删除的条目
            return NotFound(new
            {
                message = "购物车条目不存在"
            });
        }

        // 删除已直接写入数据库，无需调用 SaveChangesAsync
        return NoContent();
    }


    // 修改本人购物车条目的最终数量，不扣减商品库存
    [HttpPut("{id:int}/quantity")]
    public async Task<IActionResult> UpdateQuantity(
        int id,
        [FromBody] UpdateCartItemQuantityRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中读取用户编号
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 检查修改后的最终数量
        if (request.Quantity is null || request.Quantity.Value <= 0)
        {
            return BadRequest(new
            {
                message = "购买数量必须大于 0"
            });
        }

        int targetQuantity = request.Quantity.Value;

        // 同时按条目编号和用户编号查询，禁止修改其他人的购物车
        CartItem? cartItem = await _db.CartItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                cancellationToken);

        if (cartItem is null)
        {
            return NotFound(new
            {
                message = "购物车条目不存在"
            });
        }

        // 查询当前 SKU，使用后端库存进行校验
        ProductSku? sku = await _db.ProductSkus
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == cartItem.SkuId,
                cancellationToken);

        if (sku is null)
        {
            return NotFound(new
            {
                message = "商品规格不存在"
            });
        }

        // 商品下架后不能调整购买数量
        bool productOnSale = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == sku.ProductId
                    && product.IsOnSale,
                cancellationToken);

        if (!productOnSale)
        {
            return Conflict(new
            {
                message = "商品不存在或已下架"
            });
        }

        // 停用的 SKU 不能调整购买数量
        if (!sku.IsActive)
        {
            return Conflict(new
            {
                message = "该商品规格已停用"
            });
        }

        // 允许将原本超过库存的数量调低，但最终数量不能超过库存
        if (targetQuantity > sku.Stock)
        {
            return Conflict(new
            {
                message = "库存不足，请减少购买数量"
            });
        }

        // 数量相同时直接返回，重复提交不会继续增加数量
        if (cartItem.Quantity == targetQuantity)
        {
            return Ok(new
            {
                id = cartItem.Id,
                quantity = targetQuantity
            });
        }

        // 保存查询时的数量，避免覆盖处理期间发生的并发修改
        int expectedQuantity = cartItem.Quantity;

        // 只修改本人条目的数量，保留 SKU、勾选状态和创建时间
        int affectedRows = await _db.CartItems
            .Where(item => item.Id == id
                && item.UserId == userId
                && item.Quantity == expectedQuantity)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.Quantity,
                    targetQuantity),
                cancellationToken);

        if (affectedRows == 0)
        {
            return Conflict(new
            {
                message = "购物车已发生变化，请刷新后重试"
            });
        }

        // 返回修改后的数量；ExecuteUpdateAsync 已直接写入数据库
        return Ok(new
        {
            id = cartItem.Id,
            quantity = targetQuantity
        });
    }


    // 加入购物车，同一用户的同一 SKU 累加数量
    [HttpPost]
    public async Task<IActionResult> Add(
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中读取用户编号
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

        // 确认登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);

        if (!userExists)
        {
            return Unauthorized();
        }

        // 检查请求中的 SKU 编号和数量
        if (request.SkuId is null || request.SkuId.Value <= 0
            || request.Quantity is null || request.Quantity.Value <= 0)
        {
            return BadRequest(new
            {
                message = "SKU 编号和购买数量必须大于 0"
            });
        }

        int skuId = request.SkuId.Value;
        int quantity = request.Quantity.Value;

        // 查询具体 SKU，购物车中不保存客户端提交的价格
        ProductSku? sku = await _db.ProductSkus
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == skuId,
                cancellationToken);

        if (sku is null)
        {
            return NotFound(new
            {
                message = "商品规格不存在"
            });
        }

        // 商品必须处于上架状态
        bool productOnSale = await _db.Products
            .AsNoTracking()
            .AnyAsync(
                product => product.Id == sku.ProductId
                    && product.IsOnSale,
                cancellationToken);

        if (!productOnSale)
        {
            return Conflict(new
            {
                message = "商品不存在或已下架"
            });
        }

        // 停用的 SKU 不能加入购物车
        if (!sku.IsActive)
        {
            return Conflict(new
            {
                message = "该商品规格已停用"
            });
        }

        // 只查询当前用户的购物车条目
        CartItem? cartItem = await _db.CartItems
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId
                    && item.SkuId == skuId,
                cancellationToken);

        // 使用 long 计算累加数量，避免整数溢出
        long totalQuantity = (long)(cartItem?.Quantity ?? 0) + quantity;

        // 已有数量加上本次数量，不能超过查询到的库存
        if (totalQuantity > sku.Stock)
        {
            return Conflict(new
            {
                message = $"库存不足，购物车数量合计不能超过 {sku.Stock} 件"
            });
        }

        // 库存是 int，经过库存校验后可以安全转换
        int targetQuantity = (int)totalQuantity;

        try
        {
            if (cartItem is null)
            {
                // 创建当前用户的购物车条目，默认勾选
                cartItem = new CartItem
                {
                    UserId = userId,
                    SkuId = skuId,
                    Quantity = targetQuantity,
                    IsSelected = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.CartItems.Add(cartItem);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // 记录查询时的数量，避免并发操作覆盖其他请求
                int expectedQuantity = cartItem.Quantity;
                int cartItemId = cartItem.Id;

                // 只有数量未被其他请求修改时才更新
                int affectedRows = await _db.CartItems
                    .Where(item => item.Id == cartItemId
                        && item.UserId == userId
                        && item.Quantity == expectedQuantity)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.Quantity,
                            targetQuantity),
                        cancellationToken);

                if (affectedRows == 0)
                {
                    return Conflict(new
                    {
                        message = "购物车已发生变化，请重试"
                    });
                }
            }
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1062
                || mysqlException.Number == 1452))
        {
            // 同时新增相同 SKU 或关联数据变化时，提示重新操作
            return Conflict(new
            {
                message = "购物车或商品信息已发生变化，请重试"
            });
        }

        // 返回加入后的条目信息，商品库存保持不变
        return Ok(new
        {
            id = cartItem.Id,
            skuId = cartItem.SkuId,
            quantity = targetQuantity,
            isSelected = cartItem.IsSelected
        });
    }
}