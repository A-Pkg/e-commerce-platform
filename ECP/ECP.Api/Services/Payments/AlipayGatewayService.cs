using System.Globalization;
using Aop.Api;
using Aop.Api.Domain;
using Aop.Api.Request;
using Aop.Api.Response;

namespace ECP.Api.Services.Payments;

/// <summary>
/// 与支付宝交互，负责生成收银台表单、查询交易和关闭交易
/// </summary>
public class AlipayGatewayService
{
    // 由依赖注入提供的支付宝 SDK 客户端
    private readonly IAopClient _client;

    // 初始化支付宝客户端
    public AlipayGatewayService(IAopClient client)
    {
        _client = client;
    }

    // 生成新窗口收银台使用的 HTML 表单
    // paymentNo 使用本系统的支付编号，重试时保持不变
    // amount 和 expiresAtUtc 由后端读取订单后传入
    public string CreatePaymentForm(
        string paymentNo,
        string orderNo,
        decimal amount,
        DateTime expiresAtUtc)
    {
        // 创建电脑网站支付请求
        AlipayTradePagePayRequest request =
            new AlipayTradePagePayRequest();

        // 创建支付参数，金额和截止时间使用订单中的固定值
        AlipayTradePagePayModel model =
            new AlipayTradePagePayModel
            {
                OutTradeNo = paymentNo,
                Subject = "ECP商城订单" + orderNo,
                ProductCode = "FAST_INSTANT_TRADE_PAY",

                // 金额固定使用小数点和两位小数
                TotalAmount = amount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture),

                // 订单保存 UTC，支付宝参数使用北京时间
                // 使用绝对截止时间，重复打开收银台不会延长付款期限
                TimeExpire = expiresAtUtc
                    .AddHours(8)
                    .ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture)
            };

        request.SetBizModel(model);

        // SDK 生成签名后的表单，浏览器提交后进入支付宝收银台
        AlipayTradePagePayResponse response =
            _client.pageExecute(request);

        return response.Body;
    }

    // 按本系统支付编号查询支付宝交易
    public AlipayTradeQueryResponse QueryPayment(string paymentNo)
    {
        // 创建交易查询请求
        AlipayTradeQueryRequest request =
            new AlipayTradeQueryRequest();

        // 创建查询参数，与发起支付时使用相同的支付编号
        AlipayTradeQueryModel model =
            new AlipayTradeQueryModel
            {
                OutTradeNo = paymentNo
            };

        request.SetBizModel(model);

        return _client.Execute(request);
    }

    // 按本系统支付编号关闭支付宝交易
    public AlipayTradeCloseResponse ClosePayment(string paymentNo)
    {
        // 创建交易关闭请求
        AlipayTradeCloseRequest request =
            new AlipayTradeCloseRequest();

        // 创建关闭参数，只处理指定支付编号对应的交易
        AlipayTradeCloseModel model =
            new AlipayTradeCloseModel
            {
                OutTradeNo = paymentNo
            };

        request.SetBizModel(model);

        return _client.Execute(request);
    }
}