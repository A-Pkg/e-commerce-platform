using Aop.Api.Response;
using ECP.Api.Constants.Orders;
using ECP.Api.Constants.Payments;
using ECP.Api.Data;
using ECP.Api.Dtos.Payments;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;
using System.Data;

namespace ECP.Api.Services.Payments;

/// <summary>
/// 订单付款业务，负责付款准备、复用支付记录和查询付款结果
/// </summary>
public class OrderPaymentService
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 支付宝网关服务
    private readonly AlipayGatewayService _alipayGateway;

    // 初始化数据库上下文和支付宝网关
    public OrderPaymentService(
        AppDbContext db,
        AlipayGatewayService alipayGateway)
    {
        _db = db;
        _alipayGateway = alipayGateway;
    }

    // 为本人订单准备付款，用户编号由控制器从已验证身份取得
    public async Task<OrderPaymentPreparationResult> PrepareAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return OrderPaymentPreparationResult.Fail(
                OrderPaymentFailure.InvalidInput,
                "订单编号必须大于零");
        }

        if (userId <= 0)
        {
            return OrderPaymentPreparationResult.Fail(
                OrderPaymentFailure.UserNotFound,
                "当前登录身份无效，请重新登录");
        }

        // 确认当前登录用户仍然存在
        bool userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);

        if (!userExists)
        {
            return OrderPaymentPreparationResult.Fail(
                OrderPaymentFailure.UserNotFound,
                "当前登录用户不存在，请重新登录");
        }

        try
        {
            // 创建支付记录和零元订单付款状态必须一起提交
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            // 与取消、超时关闭锁定同一订单行，所有参数均通过 SQL 参数传入
            List<Order> lockedOrders = await _db.Orders
                .FromSqlInterpolated(
                    $"SELECT * FROM `orders` WHERE `Id` = {orderId} AND `UserId` = {userId} FOR UPDATE")
                .ToListAsync(cancellationToken);

            if (lockedOrders.Count == 0)
            {
                return OrderPaymentPreparationResult.Fail(
                    OrderPaymentFailure.OrderNotFound,
                    "订单不存在");
            }

            Order order = lockedOrders[0];

            // 已支付订单返回原成功记录，不受原付款期限影响
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

                return OrderPaymentPreparationResult.Success(
                    CreateResponse(order, existingPayment, null));
            }

            if (order.Status != OrderStatuses.PendingPayment)
            {
                return OrderPaymentPreparationResult.Fail(
                    OrderPaymentFailure.NotPendingPayment,
                    "当前订单状态不支持付款");
            }

            // 取得订单锁后，再判断付款是否已经超时
            DateTime nowUtc = DateTime.UtcNow;

            if (order.ExpiresAtUtc <= nowUtc)
            {
                return OrderPaymentPreparationResult.Fail(
                    OrderPaymentFailure.Expired,
                    "订单已超过付款期限，无法付款");
            }

            bool isZeroAmount = order.PayableAmount == 0m;
            Payment? currentPayment = null;

            if (!isZeroAmount)
            {
                // 订单锁使同一订单的付款准备依次处理，复用原待支付记录
                currentPayment = await _db.Payments
                    .SingleOrDefaultAsync(
                        payment => payment.OrderId == order.Id
                            && payment.Provider == PaymentProviders.Alipay
                            && payment.Status == PaymentStatuses.Pending,
                        cancellationToken);
            }

            if (currentPayment is null)
            {
                // 创建支付记录，金额从订单读取，不接收前端金额
                currentPayment = new Payment
                {
                    OrderId = order.Id,
                    PaymentNo = "P" + Guid.NewGuid().ToString("N"),
                    Provider = isZeroAmount
                        ? PaymentProviders.Local
                        : PaymentProviders.Alipay,
                    Status = isZeroAmount
                        ? PaymentStatuses.Succeeded
                        : PaymentStatuses.Pending,
                    Amount = order.PayableAmount,
                    CreatedAtUtc = nowUtc,
                    PaidAtUtc = isZeroAmount ? nowUtc : null
                };

                _db.Payments.Add(currentPayment);
            }

            string? paymentForm = null;

            if (isZeroAmount)
            {
                // 零元订单在本系统完成支付，不打开支付宝
                order.Status = OrderStatuses.Paid;
                order.PaidAtUtc = nowUtc;
            }
            else
            {
                // 这里只在本地生成签名表单，不向支付宝发送网络请求
                // 在表单返回给浏览器前，先保证对应支付记录已经提交
                paymentForm = _alipayGateway.CreatePaymentForm(
                    currentPayment.PaymentNo,
                    order.OrderNo,
                    currentPayment.Amount,
                    order.ExpiresAtUtc);
            }

            // 付款准备不再次扣减库存，库存已在创建订单时扣减
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return OrderPaymentPreparationResult.Success(
                CreateResponse(order, currentPayment, paymentForm));
        }
        catch (MySqlException exception) when (
            exception.Number == 1213 || exception.Number == 1205)
        {
            return OrderPaymentPreparationResult.Fail(
                OrderPaymentFailure.Busy,
                "订单正在处理中，请稍后重试");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1213 || mysqlException.Number == 1205))
        {
            return OrderPaymentPreparationResult.Fail(
                OrderPaymentFailure.Busy,
                "订单正在处理中，请稍后重试");
        }
    }


    // 查询本人订单的支付结果，网络请求期间不持有订单锁
    public async Task<OrderPaymentQueryResult> QueryAsync(
        int orderId,
        int userId,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.InvalidInput,
                "订单编号必须大于零");
        }

        if (userId <= 0 || !await _db.Users.AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.UserNotFound,
                "当前登录身份无效，请重新登录");
        }

        // 只读取本人订单，不跟踪查询前的状态快照
        Order? snapshotOrder = await _db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order => order.Id == orderId && order.UserId == userId,
                cancellationToken);

        if (snapshotOrder is null)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.OrderNotFound,
                "订单不存在");
        }

        // 已经确认付款的订单直接返回原记录，保留原支付时间
        if (snapshotOrder.Status == OrderStatuses.Paid
            || snapshotOrder.Status == OrderStatuses.Shipped
            || snapshotOrder.Status == OrderStatuses.Completed)
        {
            Payment paidPayment = await _db.Payments
                .AsNoTracking()
                .SingleAsync(
                    payment => payment.OrderId == orderId
                        && payment.Status == PaymentStatuses.Succeeded,
                    cancellationToken);

            return OrderPaymentQueryResult.Success(
                CreateQueryResponse(
                    snapshotOrder,
                    paidPayment,
                    "订单已付款"));
        }

        if (snapshotOrder.Status != OrderStatuses.PendingPayment)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.NotPendingPayment,
                "当前订单状态不支持确认付款，请刷新订单");
        }

        // 读取最近一次支付宝支付尝试
        Payment? snapshotPayment = await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.OrderId == orderId
                && payment.Provider == PaymentProviders.Alipay)
            .OrderByDescending(payment => payment.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshotPayment is null)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.PaymentNotFound,
                "尚未发起付款，请先点击去付款");
        }

        if (snapshotPayment.Status == PaymentStatuses.Closed)
        {
            return OrderPaymentQueryResult.Success(
                CreateQueryResponse(
                    snapshotOrder,
                    snapshotPayment,
                    "本次支付已关闭"));
        }

        if (snapshotPayment.Status != PaymentStatuses.Pending)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.StateChanged,
                "支付记录状态已变化，请刷新订单");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // SDK 验证支付宝响应签名；网络或验签异常交给后续接口层处理
        // 此时尚未开启数据库事务，也没有修改支付记录
        AlipayTradeQueryResponse channelResponse =
            _alipayGateway.QueryPayment(snapshotPayment.PaymentNo);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 查询完成后再开启事务，原子更新支付记录和订单
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            List<Order> lockedOrders = await _db.Orders
                .FromSqlInterpolated(
                    $"SELECT * FROM `orders` WHERE `Id` = {orderId} AND `UserId` = {userId} FOR UPDATE")
                .ToListAsync(cancellationToken);

            if (lockedOrders.Count == 0)
            {
                return OrderPaymentQueryResult.Fail(
                    OrderPaymentFailure.OrderNotFound,
                    "订单不存在");
            }

            Order currentOrder = lockedOrders[0];

            // 其他请求可能已经确认成功，返回原成功记录，避免重复处理
            if (currentOrder.Status == OrderStatuses.Paid
                || currentOrder.Status == OrderStatuses.Shipped
                || currentOrder.Status == OrderStatuses.Completed)
            {
                Payment paidPayment = await _db.Payments
                    .AsNoTracking()
                    .SingleAsync(
                        payment => payment.OrderId == orderId
                            && payment.Status == PaymentStatuses.Succeeded,
                        cancellationToken);

                return OrderPaymentQueryResult.Success(
                    CreateQueryResponse(
                        currentOrder,
                        paidPayment,
                        "订单已付款"));
            }

            if (currentOrder.Status != OrderStatuses.PendingPayment)
            {
                return OrderPaymentQueryResult.Fail(
                    OrderPaymentFailure.StateChanged,
                    "订单状态已变化，请刷新订单后重新确认付款结果");
            }

            Payment currentPayment = await _db.Payments.SingleAsync(
                payment => payment.Id == snapshotPayment.Id
                    && payment.OrderId == orderId,
                cancellationToken);

            if (currentPayment.Status != PaymentStatuses.Pending)
            {
                return OrderPaymentQueryResult.Fail(
                    OrderPaymentFailure.StateChanged,
                    "支付记录已更新，请重新查询");
            }

            // 与取消、超时关单共用校验及状态更新逻辑
            AlipayQueryState state = AlipayPaymentStateProcessor.Apply(
                currentOrder, currentPayment, channelResponse);

            if (state == AlipayQueryState.Unavailable)
            {
                return OrderPaymentQueryResult.Fail(
                    OrderPaymentFailure.GatewayUnavailable,
                    "暂时无法确认支付宝付款结果，请稍后重试");
            }

            if (state == AlipayQueryState.Mismatch)
            {
                return OrderPaymentQueryResult.Fail(
                    OrderPaymentFailure.GatewayDataMismatch,
                    "支付宝返回的交易信息未通过核对，暂未更新付款状态");
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            string message = state switch
            {
                AlipayQueryState.NotCreated => "支付宝交易尚未创建，暂未确认付款",
                AlipayQueryState.Waiting => "等待买家付款",
                AlipayQueryState.Succeeded => "付款成功",
                AlipayQueryState.Closed => "本次支付已关闭",
                _ => "查询已完成"
            };

            return OrderPaymentQueryResult.Success(
                CreateQueryResponse(
                    currentOrder,
                    currentPayment,
                    message));
        }
        catch (MySqlException exception) when (
            exception.Number == 1213 || exception.Number == 1205)
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.Busy,
                "订单正在处理中，请稍后重试");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1213
                || mysqlException.Number == 1205))
        {
            return OrderPaymentQueryResult.Fail(
                OrderPaymentFailure.Busy,
                "订单正在处理中，请稍后重试");
        }
    }

    // 多处查询分支共用响应映射
    private static QueryOrderPaymentResponse CreateQueryResponse(
        Order order,
        Payment payment,
        string message)
    {
        // 创建查询响应，保留已经保存的支付时间
        return new QueryOrderPaymentResponse
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
                : null,
            Message = message
        };
    }


    // 已支付订单和本次付款准备共用响应映射
    private static PrepareOrderPaymentResponse CreateResponse(
        Order order,
        Payment payment,
        string? paymentForm)
    {
        // 创建付款准备响应，不修改订单和支付记录
        return new PrepareOrderPaymentResponse
        {
            OrderId = order.Id,
            OrderNo = order.OrderNo,
            OrderStatus = order.Status,
            PaymentNo = payment.PaymentNo,
            Provider = payment.Provider,
            PaymentStatus = payment.Status,
            Amount = payment.Amount,
            ExpiresAtUtc = DateTime.SpecifyKind(
                order.ExpiresAtUtc,
                DateTimeKind.Utc),
            PaidAtUtc = payment.PaidAtUtc.HasValue
                ? DateTime.SpecifyKind(payment.PaidAtUtc.Value, DateTimeKind.Utc)
                : null,
            PaymentForm = paymentForm
        };
    }
}