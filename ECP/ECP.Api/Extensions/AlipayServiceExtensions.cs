using ECP.Api.Filters.Payments;
﻿using Aop.Api;
using ECP.Api.Options.Payments;
using ECP.Api.Services.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ECP.Api.Extensions;

/// <summary>
/// 支付宝配置及客户端的注册扩展
/// </summary>
public static class AlipayServiceExtensions
{
    // 注册配置校验器、配置绑定和支付宝 SDK 客户端
    public static IServiceCollection AddAlipayPayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册独立的配置校验器
        services.AddSingleton<
            IValidateOptions<AlipayOptions>,
            AlipayOptionsValidator>();

        // 绑定配置，在应用启动时执行必填检查
        services.AddOptions<AlipayOptions>()
            .Bind(configuration.GetSection("Alipay"))
            .ValidateOnStart();

        // 同一请求作用域内共用支付宝客户端
        services.AddScoped<IAopClient>(
            (IServiceProvider serviceProvider) =>
            {
                // 取得经过校验的支付宝配置
                AlipayOptions options = serviceProvider
                    .GetRequiredService<IOptions<AlipayOptions>>()
                    .Value;

                // 创建采用 RSA2 公钥模式的 SDK 配置
                AlipayConfig config = new AlipayConfig
                {
                    ServerUrl = options.GatewayUrl,
                    AppId = options.AppId,
                    PrivateKey = options.AppPrivateKey,
                    AlipayPublicKey = options.AlipayPublicKey,
                    Format = "json",
                    Charset = "utf-8",
                    SignType = "RSA2"
                };

                // 创建供支付、查询及关闭交易使用的客户端
                return new DefaultAopClient(config);
            });

        // 注册支付宝网关服务，与客户端使用相同的请求作用域
        services.AddScoped<AlipayGatewayService>();

        // 注册订单付款业务服务
        services.AddScoped<OrderPaymentService>();

        // 为付款和查询接口处理已知渠道异常
        services.AddScoped<PaymentGatewayExceptionFilter>();

        return services;
    }
}