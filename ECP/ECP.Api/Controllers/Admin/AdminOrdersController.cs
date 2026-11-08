using ECP.Api.Constants.Orders;
using ECP.Api.Constants.Users;
using ECP.Api.Data;
using ECP.Api.Dtos.Common;
using ECP.Api.Dtos.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECP.Api.Controllers.Admin;

/// <summary>
/// 管理员订单查询接口，允许查询所有用户的订单
/// </summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = UserRoles.Admin)]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public class AdminOrdersController : ControllerBase
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 初始化数据库上下文
    public AdminOrdersController(AppDbContext db)
    {
        _db = db;
    }


    // 按条件分页查询订单，最新订单排在前面
    [HttpGet]
    [ProducesResponseType(
        typeof(PagedResponse<AdminOrderListItemResponse>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword,
        [FromQuery] string? status,
        [FromQuery] int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        // 当前阶段每页固定返回最多 20 条订单
        const int pageSize = 5;

        if (pageNumber <= 0)
        {
            // 创建页码无效的响应
            return BadRequest(new
            {
                message = "页码必须大于零"
            });
        }

        // 未填写或纯空格表示不使用对应条件
        string searchKeyword = keyword?.Trim() ?? string.Empty;
        string selectedStatus = status?.Trim() ?? string.Empty;

        // 状态筛选沿用项目中已有的五种订单状态
        if (selectedStatus.Length > 0
            && selectedStatus != OrderStatuses.PendingPayment
            && selectedStatus != OrderStatuses.Paid
            && selectedStatus != OrderStatuses.Shipped
            && selectedStatus != OrderStatuses.Completed
            && selectedStatus != OrderStatuses.Cancelled)
        {
            // 创建状态无效的响应
            return BadRequest(new
            {
                message = "订单状态无效"
            });
        }

        // 构建查询，关联用户账号并选择列表所需字段
        IQueryable<AdminOrderListItemResponse> query = _db.Orders
            .AsNoTracking()
            .Join(
                _db.Users.AsNoTracking(),
                order => order.UserId,
                user => user.Id,

                // 创建管理员订单列表响应
                (order, user) => new AdminOrderListItemResponse
                {
                    Id = order.Id,
                    OrderNo = order.OrderNo,
                    Username = user.Username,
                    Status = order.Status,
                    PayableAmount = order.PayableAmount,
                    CreatedAtUtc = order.CreatedAtUtc,

                    // 在数据库中汇总该订单的商品件数
                    TotalQuantity = _db.OrderItems
                        .Where(item => item.OrderId == order.Id)
                        .Sum(item => (int?)item.Quantity) ?? 0
                });

        // 关键词匹配订单号或用户账号
        if (searchKeyword.Length > 0)
        {
            query = query.Where(order =>
                order.OrderNo.Contains(searchKeyword)
                || order.Username.Contains(searchKeyword));
        }

        // 状态条件与关键词条件同时生效
        if (selectedStatus.Length > 0)
        {
            query = query.Where(order =>
                order.Status == selectedStatus);
        }

        // 先统计满足筛选条件的全部订单数量
        int totalCount = await query.CountAsync(cancellationToken);

        // 向上取整计算总页数；空列表仍保留第 1 页
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize));

        // 请求页码超过范围时，返回最后一个有效页
        int currentPageNumber = Math.Min(pageNumber, totalPages);
        int skipCount = (currentPageNumber - 1) * pageSize;

        // 先排序再分页，只从数据库读取当前页订单
        List<AdminOrderListItemResponse> orders = await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .ThenByDescending(order => order.Id)
            .Skip(skipCount)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // 数据库保存的是 UTC 时间，读取后补充 UTC 标记
        foreach (AdminOrderListItemResponse order in orders)
        {
            order.CreatedAtUtc = DateTime.SpecifyKind(
                order.CreatedAtUtc,
                DateTimeKind.Utc);
        }

        // 创建订单分页响应，将列表元素类型指定为管理员订单概要
        PagedResponse<AdminOrderListItemResponse> response =
            new PagedResponse<AdminOrderListItemResponse>
            {
                PageNumber = currentPageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = orders
            };

        return Ok(response);
    }


    // 查询指定订单详情，管理员不受本人订单范围限制
    [HttpGet("{id:int}")]
    [ProducesResponseType(
        typeof(AdminOrderDetailResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        // 查询指定订单并关联用户账号
        AdminOrderDetailResponse? response = await _db.Orders
            .AsNoTracking()
            .Where(order => order.Id == id)
            .Join(
                _db.Users.AsNoTracking(),
                order => order.UserId,
                user => user.Id,

                // 创建详情响应，交易和收货信息读取订单快照
                (order, user) => new AdminOrderDetailResponse
                {
                    Id = order.Id,
                    OrderNo = order.OrderNo,
                    Username = user.Username,
                    Status = order.Status,
                    ItemsAmount = order.ItemsAmount,
                    ShippingFee = order.ShippingFee,
                    PayableAmount = order.PayableAmount,
                    RecipientName = order.RecipientName,
                    RecipientPhone = order.RecipientPhone,
                    ShippingAddress = order.ShippingAddress,
                    Remark = order.Remark,
                    ShippingCompany = order.ShippingCompany,
                    TrackingNumber = order.TrackingNumber,
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
            // 创建订单不存在的响应
            return NotFound(new
            {
                message = "订单不存在"
            });
        }

        // 查询下单时保存的商品明细，不关联当前商品或图片
        response.Items = await _db.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == response.Id)
            .OrderBy(item => item.Id)

            // 创建商品明细响应，复用现有 DTO
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

        // 为必填时间补充 UTC 标记
        response.CreatedAtUtc = DateTime.SpecifyKind(
            response.CreatedAtUtc,
            DateTimeKind.Utc);

        response.ExpiresAtUtc = DateTime.SpecifyKind(
            response.ExpiresAtUtc,
            DateTimeKind.Utc);

        // 可空时间只有实际存在时才转换，保留未发生状态的 null
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


    // 直接发货，只更新订单状态和发货时间
    [HttpPost("{id:int}/ship")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Ship(
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

        // 记录本次发货时间，统一使用 UTC
        DateTime shippedAtUtc = DateTime.UtcNow;

        // 将状态条件放入更新语句，只有已支付订单能够成功发货
        // 状态和时间在同一条语句中更新，重复请求不会覆盖原发货时间
        int affectedRows = await _db.Orders
            .Where(order => order.Id == id
                && order.Status == OrderStatuses.Paid)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        order => order.Status,
                        OrderStatuses.Shipped)
                    .SetProperty(
                        order => order.ShippedAtUtc,
                        (DateTime?)shippedAtUtc),
                cancellationToken);

        if (affectedRows > 0)
        {
            // 发货成功，不重复扣库存，也不修改支付记录
            return NoContent();
        }

        // 更新未成功时，区分订单不存在与状态不允许发货
        bool orderExists = await _db.Orders
            .AsNoTracking()
            .AnyAsync(
                order => order.Id == id,
                cancellationToken);

        if (!orderExists)
        {
            // 创建订单不存在的响应
            return NotFound(new
            {
                message = "订单不存在"
            });
        }

        // 待支付、已发货、已完成、已取消均不能再次执行发货
        return Conflict(new
        {
            message = "只有已支付订单可以发货，请刷新订单后重试"
        });
    }
}