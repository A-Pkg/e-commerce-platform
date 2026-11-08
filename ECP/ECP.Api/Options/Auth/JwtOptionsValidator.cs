using Microsoft.Extensions.Options;

namespace ECP.Api.Options.Auth;

public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name,JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return ValidateOptionsResult.Fail("Jwt:Issuer 不能为空");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            return ValidateOptionsResult.Fail("Jwt:Audience 不能为空");
        }

        if (options.ExpiresMinutes <= 0)
        {
            return ValidateOptionsResult.Fail("Jwt:ExpiresMinutes 必须大于 0");
        }

        if (options.SessionDays <= 0)
        {
            return ValidateOptionsResult.Fail("Jwt:SessionDays 必须大于 0");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKeyBase64))
        {
            return ValidateOptionsResult.Fail("Jwt:SigningKeyBase64 不能为空");
        }

        try
        {
            byte[] keyBytes = Convert.FromBase64String(options.SigningKeyBase64);

            if (keyBytes.Length < 32)
            {
                return ValidateOptionsResult.Fail("JWT 签名密钥解码后至少为 32 字节");
            }
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("Jwt:SigningKeyBase64 必须是有效的 Base64");
        }

        return ValidateOptionsResult.Success;
    }
}