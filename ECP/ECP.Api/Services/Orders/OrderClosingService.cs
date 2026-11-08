using System.Data;
using System.IO;
using System.Net;
using Aop.Api;
using Aop.Api.Response;
using ECP.Api.Constants.Orders;
using ECP.Api.Constants.Payments;
using ECP.Api.Constants.Stocks;
using ECP.Api.Data;
using ECP.Api.Models.Orders;
using ECP.Api.Models.Payments;
using ECP.Api.Models.Stocks;
using ECP.Api.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

namespace ECP.Api.Services.Orders;

/// <summary>
/// 协调支付渠道与订单关闭，确认不能再付款后归还库存
/// </summary>
public class OrderClosingService
{
    // 数据库上下文
    private readonly AppDbContext _db;

    // 支付宝网关
    private readonly AlipayGatewayService _alipayGateway;

    // 记录渠道异常，便于排查和后续重试
    private readonly ILogger<OrderClosingService> _logger;

    // 初始化数据库上下文、支付宝网关和日志
    public OrderClosingService(
        AppDbContext db,
        AlipayGatewayService alipayGateway,
        ILogger<OrderClosingService> logger)
    {
        _db = db;
        _alipayGateway = alipayGateway;
        _logger = logger;
    }

    // userId 有值时只处理本人订单，为空时表示系统处理
    // onlyIfExpired 为 true 时，只处理已经到期的订单
    public async Task<OrderCloseResult> CloseAsync(
        int orderId,
        int? userId,
        bool onlyIfExpired,
        CancellationToken cancellationToken)
    {
        try
        {
            // 订单锁同时阻止付款准备新增支付尝试，以及其他请求修改状态
            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            List<Order> lockedOrders;

            if (userId.HasValue)
            {
                int ownerUserId = userId.Value;
                lockedOrders = await _db.Orders
                    .FromSqlInterpolated(
                        $"SELECT * FROM `orders` WHERE `Id` = {orderId} AND `UserId` = {ownerUserId} FOR UPDATE")
                    .ToListAsync(cancellationToken);
            }
            else
            {
                lockedOrders = await _db.Orders
                    .FromSqlInterpolated(
                        $"SELECT * FROM `orders` WHERE `Id` = {orderId} FOR UPDATE")
                    .ToListAsync(cancellationToken);
            }

            if (lockedOrders.Count == 0)
            {
                return OrderCloseResult.NotFound;
            }

            Order order = lockedOrders[0];

            if (order.Status == OrderStatuses.Cancelled)
            {
                return OrderCloseResult.AlreadyClosed;
            }

            if (order.Status != OrderStatuses.PendingPayment)
            {
                return OrderCloseResult.NotPendingPayment;
            }

            if (onlyIfExpired && order.ExpiresAtUtc > DateTime.UtcNow)
            {
                return OrderCloseResult.NotExpired;
            }

            // 与付款准备保持一致：同一订单只保留一个待确认的支付宝支付尝试
            Payment? payment = await _db.Payments.SingleOrDefaultAsync(
                item => item.OrderId == order.Id
                    && item.Provider == PaymentProviders.Alipay
                    && item.Status == PaymentStatuses.Pending,
                cancellationToken);

            if (payment is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 关单期间持有订单锁，避免检查完渠道后又生成新的付款表单
                AlipayTradeQueryResponse queryResponse =
                    _alipayGateway.QueryPayment(payment.PaymentNo);

                cancellationToken.ThrowIfCancellationRequested();

                AlipayQueryState state = AlipayPaymentStateProcessor.Apply(
                    order, payment, queryResponse);

                if (state == AlipayQueryState.Waiting)
                {
                    // 先关闭渠道交易，再取消本地订单
                    AlipayTradeCloseResponse closeResponse =
                        _alipayGateway.ClosePayment(payment.PaymentNo);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (closeResponse.Code == "10000")
                    {
                        // 关单响应的交易编号为选填，提供时必须与本次交易一致
                        if ((!string.IsNullOrEmpty(closeResponse.OutTradeNo)
                                && closeResponse.OutTradeNo != payment.PaymentNo)
                            || (!string.IsNullOrEmpty(closeResponse.TradeNo)
                                && closeResponse.TradeNo != payment.ProviderTransactionId))
                        {
                            _logger.LogError(
                                "订单 {OrderId} 的支付宝关单响应编号不一致。", orderId);
                            return OrderCloseResult.PaymentDataMismatch;
                        }

                        payment.Status = PaymentStatuses.Closed;
                        state = AlipayQueryState.Closed;
                    }
                    else
                    {
                        // 买家可能在查询与关单之间完成付款，重新查询实际结果
                        _logger.LogWarning(
                            "订单 {OrderId} 关单返回 {Code}/{SubCode}，重新查询确认。",
                            orderId, closeResponse.Code, closeResponse.SubCode);

                        queryResponse = _alipayGateway.QueryPayment(payment.PaymentNo);
                        cancellationToken.ThrowIfCancellationRequested();
                        state = AlipayPaymentStateProcessor.Apply(
                            order, payment, queryResponse);
                    }
                }

                if (state == AlipayQueryState.Succeeded)
                {
                    // 支付宝已收款，保存真实付款结果，不取消或归还库存
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return OrderCloseResult.PaymentConfirmed;
                }

                if (state == AlipayQueryState.NotCreated
                    && order.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    // 表单的绝对付款期限已到，且渠道仍没有交易，才允许本地关闭
                    payment.Status = PaymentStatuses.Closed;
                }
                else if (state != AlipayQueryState.Closed)
                {
                    if (state == AlipayQueryState.Mismatch)
                    {
                        _logger.LogError(
                            "订单 {OrderId} 的支付宝交易信息未通过核对。", orderId);
                        return OrderCloseResult.PaymentDataMismatch;
                    }

                    return state == AlipayQueryState.Unavailable
                        ? OrderCloseResult.PaymentUnavailable
                        : OrderCloseResult.PaymentPending;
                }
            }

            // 渠道核对通过后，使用实际关闭时间记录库存流水
            DateTime closedAtUtc = DateTime.UtcNow;

            // 按固定 SKU 顺序归还库存
            List<OrderItem> orderItems = await _db.OrderItems
                .AsNoTracking()
                .Where(item => item.OrderId == order.Id)
                .OrderBy(item => item.SkuId)
                .ToListAsync(cancellationToken);

            foreach (OrderItem orderItem in orderItems)
            {
                int skuId = orderItem.SkuId;
                int quantity = orderItem.Quantity;

                // 在当前库存基础上增加，保留其他购买或调整产生的变化
                // 商品下架或 SKU 停用也需要归还库存
                int affectedRows = await _db.ProductSkus
                    .Where(sku => sku.Id == skuId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            sku => sku.Stock,
                            sku => sku.Stock + quantity),
                        cancellationToken);

                if (affectedRows == 0)
                {
                    // 返回后事务释放，撤销此前已经完成的其他库存归还
                    return OrderCloseResult.StockUpdateFailed;
                }

                // 更新已经锁住该 SKU，读取本次归还后的库存余额
                int balanceAfter = await _db.ProductSkus
                    .AsNoTracking()
                    .Where(sku => sku.Id == skuId)
                    .Select(sku => sku.Stock)
                    .SingleAsync(cancellationToken);

                // 创建归还流水，手动取消和超时关闭使用相同的编号规则
                StockMovement movement = new StockMovement
                {
                    MovementNo = FormattableString.Invariant(
                        $"OC:{order.OrderNo}:{skuId}"),
                    SkuId = skuId,
                    QuantityChange = quantity,
                    BalanceAfter = balanceAfter,
                    Reason = StockMovementReasons.OrderCancellation,
                    OrderId = order.Id,

                    // 手动取消记录当前用户，系统超时关闭记录为空
                    OperatorUserId = userId,
                    CreatedAtUtc = closedAtUtc
                };

                _db.StockMovements.Add(movement);
            }

            // 保留订单和明细，只修改状态和关闭时间
            order.Status = OrderStatuses.Cancelled;
            order.CancelledAtUtc = closedAtUtc;

            // 保存状态和流水，再统一提交全部库存归还
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return OrderCloseResult.Closed;
        }
        catch (MySqlException exception) when (
            exception.Number == 1213 || exception.Number == 1205)
        {
            return OrderCloseResult.Busy;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is MySqlException mysqlException
            && (mysqlException.Number == 1213 || mysqlException.Number == 1205))
        {
            return OrderCloseResult.Busy;
        }
        catch (Exception exception) when (
            exception is AopException
                or WebException
                or IOException
                or JsonException)
        {
            // 只处理支付渠道交互中的已知异常，事务释放后回滚本地修改
            _logger.LogWarning(
                exception,
                "订单 {OrderId} 支付核对或关单失败，本次未完成取消。",
                orderId);
            return OrderCloseResult.PaymentUnavailable;
        }
    }
}
