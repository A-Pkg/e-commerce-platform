namespace ECP.Api.Options.Payments;

/// <summary>
/// 支付宝公钥模式配置，当前用于沙箱环境
/// </summary>
public class AlipayOptions
{
    // 支付宝沙箱网关地址，从沙箱控制台取得
    public string GatewayUrl { get; set; } = string.Empty;

    // 沙箱应用编号
    public string AppId { get; set; } = string.Empty;

    // 应用私钥，用于签署本系统发出的支付宝请求
    public string AppPrivateKey { get; set; } = string.Empty;

    // 支付宝公钥，用于验证支付宝响应的签名
    public string AlipayPublicKey { get; set; } = string.Empty;
}