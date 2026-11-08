using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
﻿using ECP.Api.Constants.Orders;
using ECP.Api.Constants.Payments;
using ECP.Api.Constants.Stocks;
using ECP.Api.Data;
using ECP.Api.Dtos.Orders;
using ECP.Api.Dtos.Payments;
using ECP.Api.Models.Carts;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;
using ECP.Api.Models.Products;
using ECP.Api.Models.Stocks;
using ECP.Api.Models.Users;
using ECP.Api.Options.Orders;
using ECP.Api.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using System.Data;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ECP.Api.Controllers.Orders;

/// <summary>
/// 当前登录用户的订单接口
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    // 现有 decimal(18,2) 金额字段能够保存的最大值
    private const decimal MaximumAmount = 9999999999999999.99m;

    // 数据库上下文
    private readonly AppDbContext _db;

    // 订单业务配置
    private readonly OrderOptions _orderOptions;

    // 手动取消和超时关闭共用的订单关闭服务
    private readonly OrderClosingService _orderClosingService;

    // 运行环境，仅开发环境开放模拟支付
    private readonly IWebHostEnvironment _environment;

    // 初始化数据库上下文、订单配置、公共关闭服务和运行环境
    public OrdersController(
        AppDbContext db,
        IOptions<OrderOptions> orderOptions,
        OrderClosingService orderClosingService,
        IWebHostEnvironment environment)
    {
        _db = db;
        _orderOptions = orderOptions.Value;
        _orderClosingService = orderClosingService;
        _environment = environment;
    }


    // 查询本人订单，最新下单的订单排在前面
    [HttpGet]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    [ProducesResponseType(
        typeof(List<OrderListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        // 从已验证的登录身份读取用户编号，不接收客户端 UserId
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

        // 只查询本人订单，并创建列表响应
        List<OrderListItemResponse> orders = await _db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Select(order => new OrderListItemResponse
            {
                Id = order.Id,
                OrderNo = order.OrderNo,
                Status = order.Status,
                PayableAmount = order.PayableAmount,
                CreatedAtUtc = order.CreatedAtUtc,
                ExpiresAtUtc = order.ExpiresAtUtc
            })
            .ToListAsync(cancellationToken);

        // 没有订单时正常返回空数组，不继续查询商品明细
        if (orders.Count == 0)
        {
            return Ok(orders);
        }

        // 取得本次查到的本人订单编号
        int[] orderIds = orders
            .Select(order => order.Id)
            .ToArray();

        // 一次查询这些订单的商品快照，避免逐个订单请求数据库
        List<OrderListProductRow> productRows = await _db.OrderItems
            .AsNoTracking()
            .Where(item => orderIds.Contains(item.OrderId))
            .OrderBy(item => item.OrderId)
            .ThenBy(item => item.Id)

            // 创建用于归组的数据，不读取图片或当前商品信息
            .Select(item => new OrderListProductRow
            {
                OrderId = item.OrderId,

                // 创建商品明细响应，复用现有 DTO
                Item = new OrderItemResponse
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    SkuId = item.SkuId,
                    SkuCode = item.SkuCode,
                    SpecificationText = item.SpecificationText,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity,
                    Subtotal = item.Subtotal
                }
            })
            .ToListAsync(cancellationToken);

        // 按订单编号分组，供每条订单读取自己的商品明细
        ILookup<int, OrderItemResponse> productLookup =
            productRows.ToLookup(
                row => row.OrderId,
                row => row.Item);

        foreach (OrderListItemResponse order in orders)
        {
            // 数据库保存的是 UTC 时间，补充 UTC 标记
            order.CreatedAtUtc = DateTime.SpecifyKind(
                order.CreatedAtUtc,
                DateTimeKind.Utc);

            // 明确付款截止时间的 UTC 标记
            order.ExpiresAtUtc = DateTime.SpecifyKind(
                order.ExpiresAtUtc,
                DateTimeKind.Utc);

            // 将商品明细放入所属订单
            order.Items = productLookup[order.Id].ToList();
        }

        return Ok(orders);
    }



    // 查询本人订单详情，只读取订单和明细中保存的信息
    [HttpGet("{id:int}")]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    [ProducesResponseType(
        typeof(OrderDetailResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            // 创建订单编号无效的响应
            return BadRequest(new
            {
                message = "订单编号必须大于零"
            });
        }

        // 从已验证的登录身份中取得用户编号
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

        // 同时限定订单编号和所属用户，只返回本人订单
        OrderDetailResponse? response = await _db.Orders
            .AsNoTracking()
            .Where(order => order.Id == id
                && order.UserId == userId)

            // 创建详情响应，金额和收货信息读取订单快照
            .Select(order => new OrderDetailResponse
            {
                Id = order.Id,
                OrderNo = order.OrderNo,
                Status = order.Status,
                ItemsAmount = order.ItemsAmount,
                ShippingFee = order.ShippingFee,
                PayableAmount = order.PayableAmount,
                RecipientName = order.RecipientName,
                RecipientPhone = order.RecipientPhone,
                ShippingAddress = order.ShippingAddress,
                CreatedAtUtc = order.CreatedAtUtc,
                ExpiresAtUtc = order.ExpiresAtUtc,
                PaidAtUtc = order.PaidAtUtc,
                ShippedAtUtc = order.ShippedAtUtc,
                CompletedAtUtc = order.CompletedAtUtc,
                CancelledAtUtc = order.CancelledAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            // 不区分订单不存在或属于其他用户
            return NotFound(new
            {
                message = "订单不存在"
            });
        }

        // 归属检查通过后，读取该订单的全部商品明细
        // 不关联当前商品、SKU 或地址表，避免覆盖下单时的快照
        response.Items = await _db.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == response.Id)
            .OrderBy(item => item.Id)

            // 创建商品明细响应，不返回图片字段
            .Select(item => new OrderItemResponse
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                SkuId = item.SkuId,
                SkuCode = item.SkuCode,
                SpecificationText = item.SpecificationText,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                Subtotal = item.Subtotal
            })
            .ToListAsync(cancellationToken);

        // 数据库保存的是 UTC 时间，读取后补充 UTC 标记
        response.CreatedAtUtc = DateTime.SpecifyKind(
            response.CreatedAtUtc,
            DateTimeKind.Utc);

        response.ExpiresAtUtc = DateTime.SpecifyKind(
            response.ExpiresAtUtc,
            DateTimeKind.Utc);

        if (response.PaidAtUtc.HasValue)
        {
            response.PaidAtUtc = DateTime.SpecifyKind(
                response.PaidAtUtc.Value,
                DateTimeKind.Utc);
        }

        if (response.ShippedAtUtc.HasValue)
        {
            response.ShippedAtUtc = DateTime.SpecifyKind(
                response.ShippedAtUtc.Value,
                DateTimeKind.Utc);
        }

        if (response.CompletedAtUtc.HasValue)
        {
            response.CompletedAtUtc = DateTime.SpecifyKind(
                response.CompletedAtUtc.Value,
                DateTimeKind.Utc);
        }

        if (response.CancelledAtUtc.HasValue)
        {
            response.CancelledAtUtc = DateTime.SpecifyKind(
                response.CancelledAtUtc.Value,
                DateTimeKind.Utc);
        }

        return Ok(response);
    }


    // 从本人已勾选购物车创建待支付订单
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse),StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateOrderResponse),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // 从已验证的登录身份中取得用户编号
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

        // 请求列表不能包含空条目或重复的购物车编号
        if (request.Items.Any(item => item is null)
            || request.Items
                .Select(item => item.CartItemId)
                .Distinct()
                .Count() != request.Items.Count)
        {
            // 创建商品确认信息无效的响应
            return BadRequest(new
            {
                message = "结算商品不能包含空条目或重复条目"
            });
        }

        // 统一请求标识格式，同一次重试必须复用
        string requestId = request.RequestId.ToLowerInvariant();

        // 按购物车编号保存本次商品确认信息
        Dictionary<int, CreateOrderItemRequest> requestedItems =
            request.Items.ToDictionary(item => item.CartItemId);

        try
        {
            // 下单、库存流水和购物车移除必须在同一事务中完成
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            // 锁住当前用户行，让同一用户的下单请求依次处理
            // SQL 中的用户编号通过参数传入
            List<AppUser> lockedUsers = await _db.Users
                .FromSqlInterpolated(
                    $"SELECT * FROM `users` WHERE `Id` = {userId} FOR UPDATE")
                .ToListAsync(cancellationToken);

            if (lockedUsers.Count == 0)
            {
                return Unauthorized();
            }

            // 先检查重复请求，再查询可能已经被移除的购物车
            Order? existingOrder = await _db.Orders
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    order => order.UserId == userId
                        && order.RequestId == requestId,
                    cancellationToken);

            if (existingOrder is not null)
            {
                // 重复提交返回原订单，不再扣库存或延长付款期限
                return Ok(CreateResponse(existingOrder));
            }

            // 地址必须属于当前用户，订单保存地址内容快照
            UserAddress? address = await _db.UserAddresses
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.AddressId
                        && item.UserId == userId,
                    cancellationToken);

            if (address is null)
            {
                // 不区分地址不存在或属于其他用户
                return NotFound(new
                {
                    message = "收货地址不存在，请重新选择"
                });
            }

            // 锁住本人当前已勾选的条目，避免下单期间数量或勾选被修改
            List<CartItem> cartItems = await _db.CartItems
                .FromSqlInterpolated(
                    $"SELECT * FROM `cart_items` WHERE `UserId` = {userId} AND `IsSelected` = 1 ORDER BY `Id` FOR UPDATE")
                .ToListAsync(cancellationToken);

            // 本次提交必须与当前已勾选条目及已保存数量一致
            if (cartItems.Count != requestedItems.Count
                || cartItems.Any(item =>
                    !requestedItems.ContainsKey(item.Id)
                    || requestedItems[item.Id].Quantity != item.Quantity))
            {
                // 创建购物车已经变化的响应
                return Conflict(new
                {
                    message = "购物车勾选或数量已变化，请返回购物车检查"
                });
            }

            // 保存本次涉及的 SKU 编号
            int[] skuIds = cartItems
                .Select(item => item.SkuId)
                .ToArray();

            // 读取当前 SKU，后续扣库存时再次检查价格、状态和库存
            List<ProductSku> skus = await _db.ProductSkus
                .AsNoTracking()
                .Where(sku => skuIds.Contains(sku.Id))
                .ToListAsync(cancellationToken);

            // 按 SKU 编号建立查询表
            Dictionary<int, ProductSku> skuLookup =
                skus.ToDictionary(sku => sku.Id);

            // 按固定顺序锁住所属商品，保持上架状态和商品名称稳定
            int[] productIds = skus
                .Select(sku => sku.ProductId)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            // 创建本次商品查询表
            Dictionary<int, Product> productLookup =
                new Dictionary<int, Product>();

            foreach (int productId in productIds)
            {
                List<Product> lockedProducts = await _db.Products
                    .FromSqlInterpolated(
                        $"SELECT * FROM `products` WHERE `Id` = {productId} FOR UPDATE")
                    .ToListAsync(cancellationToken);

                if (lockedProducts.Count == 0
                    || !lockedProducts[0].IsOnSale)
                {
                    // 任意商品不可购买时，阻止整批下单
                    return Conflict(new
                    {
                        message = "有商品暂不可购买，请返回购物车检查"
                    });
                }

                productLookup.Add(productId, lockedProducts[0]);
            }

            foreach (CartItem cartItem in cartItems)
            {
                if (!skuLookup.TryGetValue(
                        cartItem.SkuId,
                        out ProductSku? sku)
                    || !sku.IsActive
                    || sku.Stock < cartItem.Quantity)
                {
                    // 任意 SKU 停用或库存不足时，阻止整批下单
                    return Conflict(new
                    {
                        message = "有商品暂不可购买，请返回购物车检查"
                    });
                }

                // 客户端单价只用于确认，成交价使用数据库中的售价
                if (sku.Price != requestedItems[cartItem.Id].UnitPrice)
                {
                    // 不按用户未确认的新价格直接生成订单
                    return Conflict(new
                    {
                        message = "商品价格已变化，请刷新结算信息后确认"
                    });
                }
            }

            // 使用当前数据库价格和已保存数量计算商品金额
            decimal itemsAmount = cartItems.Sum(
                item => skuLookup[item.SkuId].Price * item.Quantity);

            if (itemsAmount > MaximumAmount)
            {
                // 金额必须能保存到现有订单金额字段
                return BadRequest(new
                {
                    message = "订单金额超出允许范围"
                });
            }

            // 本项目运费固定为零
            decimal shippingFee = 0m;

            // 一次读取本次涉及的规格文字
            List<OrderSpecificationRow> specificationRows = await (
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

                // 创建拼接规格文字的中间数据
                select new OrderSpecificationRow
                {
                    SkuId = sku.Id,
                    SpecificationName = specification.Name,
                    SpecificationValue = specificationValue.Value
                })
                .ToListAsync(cancellationToken);

            // 按 SKU 分组，供订单明细读取
            ILookup<int, OrderSpecificationRow> specificationLookup =
                specificationRows.ToLookup(row => row.SkuId);

            // 统一本次订单和流水的创建时间
            DateTime createdAtUtc = DateTime.UtcNow;

            // 创建待支付订单，复制收货信息并保存固定金额
            Order newOrder = new Order
            {
                OrderNo = "O" + Guid.NewGuid().ToString("N"),
                UserId = userId,
                RequestId = requestId,
                Status = OrderStatuses.PendingPayment,
                ItemsAmount = itemsAmount,
                ShippingFee = shippingFee,
                PayableAmount = itemsAmount + shippingFee,
                RecipientName = address.RecipientName,
                RecipientPhone = address.Phone,
                ShippingAddress = address.Address,
                CreatedAtUtc = createdAtUtc,
                ExpiresAtUtc = createdAtUtc.AddMinutes(
                    _orderOptions.PaymentTimeoutMinutes)
            };

            // 先取得订单编号，事务此时尚未提交
            _db.Orders.Add(newOrder);
            await _db.SaveChangesAsync(cancellationToken);

            // 按固定 SKU 顺序扣库存，减少不同订单之间的锁顺序冲突
            foreach (CartItem cartItem in cartItems.OrderBy(item => item.SkuId))
            {
                ProductSku sku = skuLookup[cartItem.SkuId];
                Product product = productLookup[sku.ProductId];
                int quantity = cartItem.Quantity;
                decimal unitPrice = sku.Price;

                // 扣减时再次检查售价、启用状态和足够的库存
                int affectedRows = await _db.ProductSkus
                    .Where(item => item.Id == sku.Id
                        && item.Price == unitPrice
                        && item.IsActive
                        && item.Stock >= quantity)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.Stock,
                            item => item.Stock - quantity),
                        cancellationToken);

                if (affectedRows == 0)
                {
                    // 返回后事务会回滚，包括前面已经扣减的其他 SKU
                    return Conflict(new
                    {
                        message = "商品价格、状态或库存已变化，请刷新结算信息后重试"
                    });
                }

                // 更新已经锁住该 SKU，读取本次扣减后的库存余额
                int balanceAfter = await _db.ProductSkus
                    .AsNoTracking()
                    .Where(item => item.Id == sku.Id)
                    .Select(item => item.Stock)
                    .SingleAsync(cancellationToken);

                // 创建订单明细，保存下单时的名称、规格和成交价
                OrderItem orderItem = new OrderItem
                {
                    OrderId = newOrder.Id,
                    ProductId = product.Id,
                    SkuId = sku.Id,
                    ProductName = product.Name,
                    SkuCode = sku.SkuCode,
                    SpecificationText = string.Join(
                        " / ",
                        specificationLookup[sku.Id].Select(
                            row => $"{row.SpecificationName}："
                                + row.SpecificationValue)),
                    UnitPrice = unitPrice,
                    Quantity = quantity,
                    Subtotal = unitPrice * quantity,
                    CreatedAtUtc = createdAtUtc
                };

                // 创建订单扣库存流水，关联本次订单
                StockMovement movement = new StockMovement
                {
                    MovementNo = FormattableString.Invariant(
                        $"OD:{newOrder.OrderNo}:{sku.Id}"),
                    SkuId = sku.Id,
                    QuantityChange = -quantity,
                    BalanceAfter = balanceAfter,
                    Reason = StockMovementReasons.OrderDeduction,
                    OrderId = newOrder.Id,
                    OperatorUserId = userId,
                    CreatedAtUtc = createdAtUtc
                };

                _db.OrderItems.Add(orderItem);
                _db.StockMovements.Add(movement);
            }

            // 仅移除本次已经锁定并核对过的购物车条目
            _db.CartItems.RemoveRange(cartItems);

            // 保存明细、流水和购物车删除，再统一提交事务
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                CreateResponse(newOrder));
        }
        catch (MySqlException exception) when (
            exception.Number == 1213
            || exception.Number == 1205)
        {
            // 仅处理下单并发中的死锁或锁等待超时
            return Conflict(new
            {
                message = "下单繁忙，请稍后重试"
            });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1213
                || mysqlException.Number == 1205))
        {
            // SaveChangesAsync 可能将同类数据库错误包装为 DbUpdateException
            return Conflict(new
            {
                message = "下单繁忙，请稍后重试"
            });
        }
    }


    // 确认本人已发货订单收货，保存完成时间
    [HttpPost("{id:int}/confirm-receipt")]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmReceipt(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            // 创建订单编号无效的响应
            return BadRequest(new
            {
                message = "订单编号必须大于零"
            });
        }

        // 从已验证的登录身份读取用户编号
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

        // 记录本次确认收货时间，统一使用 UTC
        DateTime completedAtUtc = DateTime.UtcNow;

        // 在同一条更新语句中限定本人和已发货状态
        // 状态与完成时间一起更新，重复提交不会覆盖原完成时间
        int affectedRows = await _db.Orders
            .Where(order => order.Id == id
                && order.UserId == userId
                && order.Status == OrderStatuses.Shipped)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        order => order.Status,
                        OrderStatuses.Completed)
                    .SetProperty(
                        order => order.CompletedAtUtc,
                        (DateTime?)completedAtUtc),
                cancellationToken);

        if (affectedRows > 0)
        {
            // 确认收货成功，不修改库存、金额或支付记录
            return NoContent();
        }

        // 只检查本人订单，不向用户透露其他账号的订单信息
        bool orderExists = await _db.Orders
            .AsNoTracking()
            .AnyAsync(
                order => order.Id == id
                    && order.UserId == userId,
                cancellationToken);

        if (!orderExists)
        {
            // 不存在和属于其他用户的订单统一返回 404
            return NotFound(new
            {
                message = "订单不存在"
            });
        }

        // 已完成订单再次提交也不会重复更新完成时间
        return Conflict(new
        {
            message = "只有已发货订单可以确认收货，请刷新订单后重试"
        });
    }




    // 取消本人的待支付订单，库存归还由公共关闭服务完成
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            // 创建订单编号无效的响应
            return BadRequest(new
            {
                message = "订单编号必须大于零"
            });
        }

        // 从已验证的登录身份中取得用户编号
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

        // 手动取消只处理本人订单，不要求订单已经超时
        OrderCloseResult result = await _orderClosingService.CloseAsync(
            orderId: id,
            userId: userId,
            onlyIfExpired: false,
            cancellationToken: cancellationToken);

        switch (result)
        {
            case OrderCloseResult.Closed:
            case OrderCloseResult.AlreadyClosed:
                return NoContent();

            case OrderCloseResult.NotFound:
                // 创建订单不存在的响应
                return NotFound(new
                {
                    message = "订单不存在"
                });

            case OrderCloseResult.NotPendingPayment:
                // 创建当前状态不允许取消的响应
                return Conflict(new
                {
                    message = "只有待支付订单可以取消"
                });

            case OrderCloseResult.PaymentConfirmed:
                return Conflict(new
                {
                    message = "已确认订单付款成功，无法取消，请刷新订单"
                });

            case OrderCloseResult.PaymentPending:
                return Accepted(new
                {
                    orderId = id,
                    message = "订单尚未取消，请稍后重试；超过付款截止时间后，系统会继续检查处理"
                });

            case OrderCloseResult.PaymentUnavailable:
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "暂时无法确认支付结果，订单尚未取消，请稍后重试"
                });

            case OrderCloseResult.PaymentDataMismatch:
                return Conflict(new
                {
                    message = "支付信息暂未通过核对，订单尚未取消"
                });

            case OrderCloseResult.StockUpdateFailed:
                // 创建库存归还未完成的响应
                return Conflict(new
                {
                    message = "订单商品数据已变化，暂时无法取消"
                });

            default:
                // 数据库锁冲突时允许用户稍后重试
                return Conflict(new
                {
                    message = "订单正在处理中，请稍后重试"
                });
        }
    }


    // 模拟支付本人订单，金额从订单读取，不再次扣减库存
    [HttpPost("{id:int}/mock-pay")]
    [ProducesResponseType(typeof(MockPaymentResponse),StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MockPay(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        // 模拟支付属于开发调试入口
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (id <= 0)
        {
            // 创建订单编号无效的响应
            return BadRequest(new
            {
                message = "订单编号必须大于零"
            });
        }

        // 从已验证的登录身份中取得用户编号
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

        try
        {
            // 支付记录与订单付款状态必须一起提交
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            // 与取消、超时关闭锁定同一订单行
            // 只允许操作本人订单，用户编号通过 SQL 参数传入
            List<Order> lockedOrders = await _db.Orders
                .FromSqlInterpolated(
                    $"SELECT * FROM `orders` WHERE `Id` = {id} AND `UserId` = {userId} FOR UPDATE")
                .ToListAsync(cancellationToken);

            if (lockedOrders.Count == 0)
            {
                // 不区分订单不存在或属于其他用户
                return NotFound(new
                {
                    message = "订单不存在"
                });
            }

            Order order = lockedOrders[0];

            // 在订单锁内检查，避免与支付宝付款准备并发时混用渠道
            bool hasAlipayPayment = await _db.Payments
                .AsNoTracking()
                .AnyAsync(
                    payment => payment.OrderId == order.Id
                        && payment.Provider == PaymentProviders.Alipay,
                    cancellationToken);

            if (hasAlipayPayment)
            {
                return Conflict(new
                {
                    message = "该订单已使用支付宝，请通过支付宝完成付款或查询付款结果"
                });
            }

            // 已支付或进入后续履约阶段的订单，返回原成功支付记录
            // 此判断放在付款期限检查前，避免已付款订单到期后重试失败
            if (order.Status == OrderStatuses.Paid
                || order.Status == OrderStatuses.Shipped
                || order.Status == OrderStatuses.Completed)
            {
                Payment existingPayment = await _db.Payments
                    .AsNoTracking()
                    .SingleAsync(
                        payment => payment.OrderId == order.Id
                            && payment.Status == PaymentStatuses.Succeeded,
                        cancellationToken);

                return Ok(CreateMockPaymentResponse(
                    order,
                    existingPayment));
            }

            if (order.Status != OrderStatuses.PendingPayment)
            {
                // 已取消订单不能重新付款
                return Conflict(new
                {
                    message = "当前订单状态不支持付款"
                });
            }

            // 取得订单锁后，再判断付款是否已经超时
            DateTime paidAtUtc = DateTime.UtcNow;

            if (order.ExpiresAtUtc <= paidAtUtc)
            {
                // 到期后立即拒绝付款，库存由超时关闭任务统一归还
                return Conflict(new
                {
                    message = "订单已超过付款期限，无法付款"
                });
            }

            // 创建成功的模拟支付记录，金额取订单中的固定应付金额
            Payment newPayment = new Payment
            {
                OrderId = order.Id,
                PaymentNo = "P" + Guid.NewGuid().ToString("N"),
                Provider = PaymentProviders.Mock,
                Status = PaymentStatuses.Succeeded,
                Amount = order.PayableAmount,
                CreatedAtUtc = paidAtUtc,
                PaidAtUtc = paidAtUtc
            };

            // 更新订单付款状态，库存已经在创建订单时扣减
            order.Status = OrderStatuses.Paid;
            order.PaidAtUtc = paidAtUtc;

            _db.Payments.Add(newPayment);

            // 支付记录和订单状态一起保存并提交
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(CreateMockPaymentResponse(order, newPayment));
        }
        catch (MySqlException exception) when (
            exception.Number == 1213
            || exception.Number == 1205)
        {
            // 仅处理并发操作中的死锁或锁等待超时
            return Conflict(new
            {
                message = "订单正在处理中，请稍后重试"
            });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1213
                || mysqlException.Number == 1205))
        {
            // SaveChangesAsync 可能包装同类数据库锁错误
            return Conflict(new
            {
                message = "订单正在处理中，请稍后重试"
            });
        }
    }


    // 首次付款和重复付款共用响应映射，保留原支付编号和成功时间
    private static MockPaymentResponse CreateMockPaymentResponse(Order order,Payment payment)
    {
        // 创建付款响应，不修改订单和支付记录
        return new MockPaymentResponse
        {
            OrderId = order.Id,
            OrderNo = order.OrderNo,
            OrderStatus = order.Status,
            PaymentNo = payment.PaymentNo,
            Provider = payment.Provider,
            PaymentStatus = payment.Status,
            Amount = payment.Amount,
            PaidAtUtc = payment.PaidAtUtc.HasValue
                ? DateTime.SpecifyKind(
                    payment.PaidAtUtc.Value,
                    DateTimeKind.Utc)
                : null
        };
    }


    // 新建成功和重复提交共用响应映射，读取数据库时间后补充 UTC 标记
    private static CreateOrderResponse CreateResponse(Order order)
    {
        // 创建订单响应，不修改数据库中的订单内容
        return new CreateOrderResponse
        {
            Id = order.Id,
            OrderNo = order.OrderNo,
            Status = order.Status,
            ItemsAmount = order.ItemsAmount,
            ShippingFee = order.ShippingFee,
            PayableAmount = order.PayableAmount,
            CreatedAtUtc = DateTime.SpecifyKind(
                order.CreatedAtUtc,
                DateTimeKind.Utc),
            ExpiresAtUtc = DateTime.SpecifyKind(
                order.ExpiresAtUtc,
                DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// 订单 SKU 规格展示中间数据
    /// </summary>
    private sealed class OrderSpecificationRow
    {
        // 所属 SKU 编号
        public int SkuId { get; set; }

        // 规格名称
        public string SpecificationName { get; set; } = string.Empty;

        // 规格值内容
        public string SpecificationValue { get; set; } = string.Empty;
    }


    /// <summary>
    /// 订单列表商品查询的中间数据，仅用于按订单组装响应
    /// </summary>
    private sealed class OrderListProductRow
    {
        // 商品所属的订单编号
        public int OrderId { get; set; }

        // 创建商品明细响应对象
        public OrderItemResponse Item { get; set; } =
            new OrderItemResponse();
    }
}


