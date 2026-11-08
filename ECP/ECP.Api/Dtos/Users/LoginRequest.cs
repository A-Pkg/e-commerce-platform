using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Users;

public class LoginRequest
{
    [Required(ErrorMessage = "请输入账号")]
    [StringLength(50, ErrorMessage = "账号不能超过 50 个字符")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [StringLength(128, ErrorMessage = "密码不能超过 128 个字符")]
    public string Password { get; set; } = string.Empty;
}