using ECP.Api.Constants.Orders;
using ECP.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace ECP.Api.Services.Orders;

/// <summary>
/// 定期扫描到期的待支付订单，调用公共关闭服务归还库存
/// </summary>
public class OrderExpirationBackgroundService : BackgroundService
{
    // 每轮最多处理 100 笔，剩余订单由后续扫描继续处理
    private const int BatchSize = 100;

    // 扫描间隔为 10 秒
    private static readonly TimeSpan ScanInterval =
        TimeSpan.FromSeconds(10);

    // 用于创建独立的数据库服务作用域
    private readonly IServiceScopeFactory _scopeFactory;

    // 记录后台关闭结果及数据库访问错误
    private readonly ILogger<OrderExpirationBackgroundService> _logger;

    // 初始化作用域工厂和日志服务
    public OrderExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // 启动后立即扫描一次，随后按固定间隔扫描
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // 创建周期计时器，服务退出时释放
        using PeriodicTimer timer = new PeriodicTimer(ScanInterval);

        try
        {
            do
            {
                try
                {
                    List<int> orderIds;

                    // 扫描只读取订单编号，读取完成后释放本次查询作用域
                    await using (
                        AsyncServiceScope scanScope =
                            _scopeFactory.CreateAsyncScope())
                    {
                        AppDbContext db = scanScope.ServiceProvider
                            .GetRequiredService<AppDbContext>();

                        DateTime nowUtc = DateTime.UtcNow;

                        orderIds = await db.Orders
                            .AsNoTracking()
                            .Where(order =>
                                order.Status == OrderStatuses.PendingPayment
                                && order.ExpiresAtUtc <= nowUtc)
                            .OrderBy(order => order.ExpiresAtUtc)
                            .ThenBy(order => order.Id)
                            .Select(order => order.Id)
                            .Take(BatchSize)
                            .ToListAsync(stoppingToken);
                    }

                    foreach (int orderId in orderIds)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        // 每笔订单使用独立上下文
                        // 某笔回滚后，不把其跟踪状态带到下一笔订单
                        await using AsyncServiceScope orderScope =
                            _scopeFactory.CreateAsyncScope();

                        OrderClosingService closingService =
                            orderScope.ServiceProvider
                                .GetRequiredService<OrderClosingService>();

                        // 系统关闭不传操作用户，并要求锁定后再次检查到期时间
                        OrderCloseResult result =
                            await closingService.CloseAsync(
                                orderId: orderId,
                                userId: null,
                                onlyIfExpired: true,
                                cancellationToken: stoppingToken);

                        if (result == OrderCloseResult.Closed)
                        {
                            _logger.LogInformation(
                                "超时订单 {OrderId} 已关闭，库存已归还。",
                                orderId);
                        }
                        else if (result == OrderCloseResult.PaymentConfirmed)
                        {
                            _logger.LogInformation(
                                "超时订单 {OrderId} 已核实付款成功，保留订单和库存扣减。",
                                orderId);
                        }
                        else if (
                            result == OrderCloseResult.Busy
                            || result == OrderCloseResult.StockUpdateFailed
                            || result == OrderCloseResult.PaymentPending
                            || result == OrderCloseResult.PaymentUnavailable
                            || result == OrderCloseResult.PaymentDataMismatch)
                        {
                            _logger.LogWarning(
                                "超时订单 {OrderId} 本轮未完成关闭，结果为 {Result}，后续扫描将再次检查。",
                                orderId,
                                result);
                        }

                        // 已取消、已支付、未到期或已不存在属于正常跳过情况
                    }
                }
                catch (MySqlException exception)
                {
                    // 数据库访问失败时记录日志，等待下一轮扫描
                    _logger.LogError(
                        exception,
                        "订单超时扫描遇到数据库访问错误，下一轮将重新扫描。");
                }
                catch (DbUpdateException exception) when (
                    exception.InnerException is MySqlException)
                {
                    // 保存过程中的数据库访问错误也在本轮结束后重试扫描
                    _logger.LogError(
                        exception,
                        "超时订单保存失败，事务已回滚，下一轮将重新扫描。");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (
            stoppingToken.IsCancellationRequested)
        {
            // 后端正常停止，结束扫描
        }
    }
}