using ECP.Api.Dtos.Auth;

namespace ECP.Api.Services.Auth;

/// <summary>
/// 服务内部的签发结果；刷新令牌仅用于写 Cookie，不作为 JSON 返回
/// </summary>
public class AuthSessionResult
{
    // 可以返回给前端的访问令牌信息
    public AccessTokenResponse Token { get; set; } = new AccessTokenResponse();

    // 仅由控制器写入 HttpOnly Cookie
    public string RefreshToken { get; set; } = string.Empty;
}