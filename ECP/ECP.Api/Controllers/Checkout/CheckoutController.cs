using ECP.Api.Data;
using ECP.Api.Dtos.Checkout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Checkout;

/// <summary>
/// 当前登录用户的结算接口
/// </summary>
[ApiController]
[Route("api/checkout")]
[Authorize]
public class CheckoutController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public CheckoutController(AppDbContext db)
    {
        _db = db;
    }

    // 预览本人已勾选的购物车商品，不创建订单或修改库存
    [HttpGet("preview")]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    [ProducesResponseType(
        typeof(CheckoutPreviewResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview(
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

        // 一次读取本人所有已勾选条目及其当前可购买状态
        // 保留不可购买条目，用于判断是否需要阻止整批结算
        List<CheckoutPreviewItemRow> rows = await (
            from cartItem in _db.CartItems.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on cartItem.SkuId equals sku.Id
            join product in _db.Products.AsNoTracking()
                on sku.ProductId equals product.Id
            where cartItem.UserId == userId
                && cartItem.IsSelected
            orderby cartItem.Id descending

            // 创建查询中间数据，购买状态仅用于后端检查
            select new CheckoutPreviewItemRow
            {
                IsAvailable = product.IsOnSale
                    && sku.IsActive
                    && sku.Stock > 0
                    && sku.Stock >= cartItem.Quantity,

                // 创建商品响应，价格和数量来自数据库
                Item = new CheckoutPreviewItemResponse
                {
                    CartItemId = cartItem.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    SkuId = sku.Id,
                    SkuCode = sku.SkuCode,
                    UnitPrice = sku.Price,
                    Quantity = cartItem.Quantity
                }
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            // 创建没有勾选商品时的错误响应
            return BadRequest(new
            {
                message = "请先勾选需要结算的商品"
            });
        }

        // 任意已勾选条目不可购买，阻止本次整批结算
        if (rows.Any(row => !row.IsAvailable))
        {
            // 创建统一错误响应，具体情况由用户返回购物车检查
            return Conflict(new
            {
                message = "有商品暂不可购买，请返回购物车检查"
            });
        }

        // 只读取本次结算涉及的 SKU 规格
        int[] skuIds = rows
            .Select(row => row.Item.SkuId)
            .ToArray();

        // 一次查询全部规格文字，避免逐条查询
        List<CheckoutSpecificationRow> specificationRows = await (
            from relation in
                _db.ProductSkuSpecificationValues.AsNoTracking()
            join sku in _db.ProductSkus.AsNoTracking()
                on relation.SkuId equals sku.Id
            join specification in
                _db.ProductSpecifications.AsNoTracking()
                on relation.SpecificationId equals specification.Id
            join specificationValue in
                _db.ProductSpecificationValues.AsNoTracking()
                on relation.SpecificationValueId
                    equals specificationValue.Id
            where skuIds.Contains(sku.Id)
                && specification.ProductId == sku.ProductId
                && specificationValue.SpecificationId
                    == relation.SpecificationId
            orderby sku.Id, specification.SortOrder, specification.Id

            // 创建用于拼接规格文字的中间数据
            select new CheckoutSpecificationRow
            {
                SkuId = sku.Id,
                SpecificationName = specification.Name,
                SpecificationValue = specificationValue.Value
            })
            .ToListAsync(cancellationToken);

        // 按 SKU 分组，供商品条目读取对应规格
        ILookup<int, CheckoutSpecificationRow> specificationLookup =
            specificationRows.ToLookup(row => row.SkuId);

        foreach (CheckoutPreviewItemRow row in rows)
        {
            // 无规格的默认 SKU 保持空字符串
            row.Item.SpecificationText = string.Join(
                " / ",
                specificationLookup[row.Item.SkuId].Select(
                    specification =>
                        $"{specification.SpecificationName}："
                        + specification.SpecificationValue));

            // 使用 decimal 在后端计算商品小计
            row.Item.Subtotal =
                row.Item.UnitPrice * row.Item.Quantity;
        }

        // 汇总商品金额，运费固定为零
        decimal itemsAmount = rows.Sum(row => row.Item.Subtotal);
        decimal shippingFee = 0m;

        // 创建结算预览响应，不保存到数据库
        CheckoutPreviewResponse response = new CheckoutPreviewResponse
        {
            Items = rows.Select(row => row.Item).ToList(),
            ItemsAmount = itemsAmount,
            ShippingFee = shippingFee,
            PayableAmount = itemsAmount + shippingFee
        };

        return Ok(response);
    }

    /// <summary>
    /// 结算商品查询中间数据，包含响应内容和可购买状态
    /// </summary>
    private sealed class CheckoutPreviewItemRow
    {
        // 商品响应，创建空对象作为默认值
        public CheckoutPreviewItemResponse Item { get; set; } =
            new CheckoutPreviewItemResponse();

        // 当前商品状态及库存是否允许购买该数量
        public bool IsAvailable { get; set; }
    }

    /// <summary>
    /// SKU 规格展示中间数据，仅用于组装规格文字
    /// </summary>
    private sealed class CheckoutSpecificationRow
    {
        // 所属 SKU 编号
        public int SkuId { get; set; }

        // 规格名称
        public string SpecificationName { get; set; } = string.Empty;

        // 规格值内容
        public string SpecificationValue { get; set; } = string.Empty;
    }
}