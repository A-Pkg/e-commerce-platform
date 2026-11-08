using Microsoft.Extensions.Options;

namespace ECP.Api.Options.Payments;

/// <summary>
/// 检查支付宝配置的必填项
/// </summary>
public class AlipayOptionsValidator : IValidateOptions<AlipayOptions>
{
    // 校验配置，缺失时返回对应字段的提示
    public ValidateOptionsResult Validate(
        string? name,
        AlipayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GatewayUrl))
        {
            return ValidateOptionsResult.Fail(
                "未配置 Alipay:GatewayUrl 沙箱网关地址");
        }

        if (string.IsNullOrWhiteSpace(options.AppId))
        {
            return ValidateOptionsResult.Fail(
                "未配置 Alipay:AppId 沙箱应用编号");
        }

        if (string.IsNullOrWhiteSpace(options.AppPrivateKey))
        {
            return ValidateOptionsResult.Fail(
                "未配置 Alipay:AppPrivateKey 应用私钥");
        }

        if (string.IsNullOrWhiteSpace(options.AlipayPublicKey))
        {
            return ValidateOptionsResult.Fail(
                "未配置 Alipay:AlipayPublicKey 支付宝公钥");
        }

        return ValidateOptionsResult.Success;
    }
}