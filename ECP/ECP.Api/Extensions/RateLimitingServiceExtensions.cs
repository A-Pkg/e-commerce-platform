using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ECP.Api.Extensions;

/// <summary>
/// 注册账号接口的请求频率限制
/// </summary>
public static class RateLimitingServiceExtensions
{
    // 注册两组限流：auth 用于注册、登录和改密
    // auth-session 用于刷新和退出，两组分别按来源 IP 计数
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // 超出限制时返回 HTTP 429
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // 返回前端能够直接显示的错误提示
            options.OnRejected = async (context, cancellationToken) =>
            {
                // 创建并返回限流提示，不包含内部信息
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "操作过于频繁，请稍后再试" },
                    cancellationToken);
            };

            // 添加名为 auth 的策略，按照来源 IP 分别计数
            options.AddPolicy("auth", context =>
            {
                // 获取后端看到的请求来源 IP
                string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // 为每个来源 IP 创建固定时间窗口限流器
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, partitionKey =>
                {
                    // 配置一分钟窗口，最多放行 10 次，不排队等待
                    return new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10, // 一个窗口内允许通过的令牌数，也就是最多 10 次请求
                        Window = TimeSpan.FromMinutes(1),  // 每个窗口持续 1分钟，窗口结束后恢复请求额度
                        QueueLimit = 0,  // 
                        AutoReplenishment = true  // 
                    };
                });
            });

            // 刷新与退出单独计数，不占用登录、注册和改密的额度
            options.AddPolicy("auth-session", context =>
            {
                string clientIp =
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    clientIp,
                    partitionKey => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }
}