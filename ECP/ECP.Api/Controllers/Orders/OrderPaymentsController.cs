using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ECP.Api.Dtos.Payments;
using ECP.Api.Filters.Payments;
using ECP.Api.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECP.Api.Controllers.Orders;

/// <summary>
/// 当前登录用户的付款准备和支付结果查询接口
/// </summary>
[ApiController]
[Route("api/orders/{orderId:int}/payments")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ServiceFilter(typeof(PaymentGatewayExceptionFilter))]
public class OrderPaymentsController : ControllerBase
{
    // 订单付款业务服务
    private readonly OrderPaymentService _paymentService;

    // 初始化订单付款业务服务
    public OrderPaymentsController(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    // 准备本人订单付款，金额和用户编号均不从请求体接收
    [HttpPost("prepare")]
    [ProducesResponseType(typeof(PrepareOrderPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Prepare(
        [FromRoute] int orderId,
        CancellationToken cancellationToken)
    {
        int? userId = GetCurrentUserId();

        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        OrderPaymentPreparationResult result = await _paymentService.PrepareAsync(
            orderId, userId.Value, cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Payment);
        }

        return CreateFailure(result.Failure, result.Message);
    }

    // 查询会同步付款状态，因此使用 POST
    [HttpPost("query")]
    [ProducesResponseType(typeof(QueryOrderPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Query(
        [FromRoute] int orderId,
        CancellationToken cancellationToken)
    {
        int? userId = GetCurrentUserId();

        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        OrderPaymentQueryResult result = await _paymentService.QueryAsync(
            orderId, userId.Value, cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Payment);
        }

        return CreateFailure(result.Failure, result.Message);
    }

    // 两个接口共用已经通过 JWT 认证的用户编号解析
    private int? GetCurrentUserId()
    {
        string? value = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int userId)
            && userId > 0
                ? userId
                : null;
    }

    // 两个接口使用相同的业务错误和 HTTP 状态映射
    private IActionResult CreateFailure(OrderPaymentFailure failure, string message)
    {
        int statusCode = failure switch
        {
            OrderPaymentFailure.InvalidInput => StatusCodes.Status400BadRequest,
            OrderPaymentFailure.UserNotFound => StatusCodes.Status401Unauthorized,
            OrderPaymentFailure.OrderNotFound => StatusCodes.Status404NotFound,
            OrderPaymentFailure.NotPendingPayment
                or OrderPaymentFailure.Expired
                or OrderPaymentFailure.Busy
                or OrderPaymentFailure.PaymentNotFound
                or OrderPaymentFailure.StateChanged => StatusCodes.Status409Conflict,
            OrderPaymentFailure.GatewayUnavailable => StatusCodes.Status503ServiceUnavailable,
            OrderPaymentFailure.GatewayDataMismatch => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };

        // 创建与现有前端错误读取方式一致的响应
        return StatusCode(statusCode, new { message });
    }
}
