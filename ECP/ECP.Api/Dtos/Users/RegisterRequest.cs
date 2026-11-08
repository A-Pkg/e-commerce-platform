using System.ComponentModel.DataAnnotations;

namespace ECP.Api.Dtos.Users;

public class RegisterRequest
{
    [Required(ErrorMessage = "请输入账号")]
    [StringLength(50, MinimumLength = 3,ErrorMessage = "账号长度必须为 3～50 个字符")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$",ErrorMessage = "账号只能包含英文字母、数字和下划线")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [StringLength(128, MinimumLength = 8,ErrorMessage = "密码长度必须为 8～128 个字符")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "请再次输入密码")]
    [Compare(nameof(Password), ErrorMessage = "两次输入的密码不一致")]
    public string ConfirmPassword { get; set; } = string.Empty;
}