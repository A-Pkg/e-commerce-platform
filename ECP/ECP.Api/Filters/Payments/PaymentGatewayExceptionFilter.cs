using System.IO;
using System.Net;
using Aop.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ECP.Api.Filters.Payments;

/// <summary>
/// 处理付款和查询接口中的已知支付渠道异常
/// </summary>
public sealed class PaymentGatewayExceptionFilter : IExceptionFilter
{
    // 记录渠道请求异常
    private readonly ILogger<PaymentGatewayExceptionFilter> _logger;

    // 初始化日志
    public PaymentGatewayExceptionFilter(ILogger<PaymentGatewayExceptionFilter> logger)
    {
        _logger = logger;
    }

    // 只处理 SDK、网络和渠道响应解析异常，其他异常继续向外传播
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (
            AopException or WebException or IOException or JsonException))
        {
            return;
        }

        _logger.LogWarning(context.Exception, "订单支付渠道请求失败，路径为 {Path}。",
            context.HttpContext.Request.Path);

        context.HttpContext.Response.Headers.CacheControl = "no-store";

        // 创建通用错误响应，不把 SDK 异常详情返回浏览器
        context.Result = new ObjectResult(new
        {
            message = "支付服务暂时不可用，请稍后重试并查询付款结果"
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };

        context.ExceptionHandled = true;
    }
}
